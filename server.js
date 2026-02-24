/**
 * BSD 3-Clause License
 *
 * Copyright (c) 2018-2022, Steve Tung
 * All rights reserved.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *
 * * Redistributions of source code must retain the above copyright notice, this
 *   list of conditions and the following disclaimer.
 *
 * * Redistributions in binary form must reproduce the above copyright notice,
 *   this list of conditions and the following disclaimer in the documentation
 *   and/or other materials provided with the distribution.
 *
 * * Neither the name of the copyright holder nor the names of its
 *   contributors may be used to endorse or promote products derived from
 *   this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
 * AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
 * IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
 * FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
 * DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
 * SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
 * CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
 * OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
 * OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

const express = require('express');
const rateLimit = require('express-rate-limit');
const path = require('path');
const fs = require('fs');
const os = require('os');
const crypto = require('crypto');
const recorder = require('./index.js');

const app = express();
const defaultPort = 3000;
const jobs = {};
const maxConcurrentRecordings = 5;
const maxBatchHtmlBytes = 40 * 1024 * 1024;
var activeRecordingCount = 0;
var pendingRecordings = [];

app.use(express.json({ limit: '50mb' }));
app.use(express.static(path.join(__dirname, 'public')));

var downloadLimiter = rateLimit({ windowMs: 60 * 1000, max: 30 });
var batchRecordLimiter = rateLimit({ windowMs: 60 * 1000, max: 10 });

function sanitizeFilename(name) {
  return String(name || '')
    .replace(/[/\\?%*:|"<>]/g, '-')
    .replace(/\s+/g, ' ')
    .trim();
}

function uniqueFilePath(targetPath) {
  if (!fs.existsSync(targetPath)) {
    return targetPath;
  }
  var parsed = path.parse(targetPath);
  var attempt = 1;
  var candidate;
  do {
    candidate = path.join(parsed.dir, parsed.name + '-' + attempt + parsed.ext);
    attempt++;
  } while (fs.existsSync(candidate));
  return candidate;
}

function uniqueDirectoryPath(targetPath) {
  if (!fs.existsSync(targetPath)) {
    return targetPath;
  }
  var attempt = 1;
  var candidate;
  do {
    candidate = targetPath + '-' + attempt;
    attempt++;
  } while (fs.existsSync(candidate));
  return candidate;
}

function getQueuePosition(jobId) {
  for (var i = 0; i < pendingRecordings.length; i++) {
    if (pendingRecordings[i].job.id === jobId) {
      return i + 1;
    }
  }
  return 0;
}

function drainRecordingQueue() {
  while (activeRecordingCount < maxConcurrentRecordings && pendingRecordings.length > 0) {
    var entry = pendingRecordings.shift();
    if (entry.job.stopped) {
      continue;
    }
    entry.job.status = 'running';
    entry.job.broadcast('running', JSON.stringify({ message: 'Capture started' }));
    activeRecordingCount++;
    entry.run()
      .then(function () {
        activeRecordingCount--;
        drainRecordingQueue();
      }, function () {
        activeRecordingCount--;
        drainRecordingQueue();
      });
  }
}

function queueRecording(job, runner) {
  job.status = 'queued';
  pendingRecordings.push({
    job: job,
    run: runner
  });
  job.broadcast('queued', JSON.stringify({ position: getQueuePosition(job.id) }));
  drainRecordingQueue();
}

function createRecordingJob(config) {
  var safeConfig = config || {};
  var jobId = crypto.randomBytes(8).toString('hex');
  var subscribers = [];
  var tmpFile = null;
  var output = path.resolve(process.cwd(), safeConfig.output || 'video.mp4');
  var captureUrl = safeConfig.url;

  if (safeConfig.htmlContent) {
    tmpFile = path.join(os.tmpdir(), 'timecut-' + jobId + '.html');
    fs.writeFileSync(tmpFile, safeConfig.htmlContent, 'utf8');
    captureUrl = tmpFile;
  }

  function broadcast(event, data) {
    var msg = 'event: ' + event + '\ndata: ' + data + '\n\n';
    subscribers.forEach(function (sub) {
      try { sub.write(msg); } catch (e) { /* ignore closed connections */ }
    });
  }

  var job = {
    id: jobId,
    status: 'queued',
    output: output,
    subscribers: subscribers,
    stopped: false,
    broadcast: broadcast
  };
  jobs[jobId] = job;

  var fps = parseFloat(safeConfig.fps) || 60;
  var duration = parseFloat(safeConfig.duration) || 5;
  var totalFrames = Math.round(fps * duration);
  var capturedFrames = 0;

  var captureConfig = Object.assign({}, safeConfig, {
    url: captureUrl,
    output: output,
    quiet: true,
    viewport: safeConfig.viewport || { width: 800, height: 600 }
  });
  delete captureConfig.htmlContent;

  captureConfig.pipeMode = false;
  captureConfig.frameProcessor = function () {
    if (!job.stopped) {
      capturedFrames++;
      broadcast('progress', JSON.stringify({ captured: capturedFrames, total: totalFrames }));
    }
  };

  queueRecording(job, function () {
    broadcast('log', 'Starting capture: ' + totalFrames + ' frames at ' + fps + ' fps…');
    return recorder(captureConfig)
      .then(function () {
        if (tmpFile) {
          try { fs.unlinkSync(tmpFile); } catch (e) { /* ignore */ }
        }
        job.status = 'done';
        broadcast('done', JSON.stringify({ output: output }));
        subscribers.forEach(function (sub) { try { sub.end(); } catch (e) { /* ignore */ } });
      })
      .catch(function (err) {
        if (tmpFile) {
          try { fs.unlinkSync(tmpFile); } catch (e) { /* ignore */ }
        }
        job.status = 'error';
        broadcast('error', String(err && err.message || err));
        subscribers.forEach(function (sub) { try { sub.end(); } catch (e) { /* ignore */ } });
      });
  });

  return { jobId: jobId, output: output };
}

/**
 * POST /record
 * Body: JSON capture config (url | htmlContent, fps, duration, viewport, …)
 * Returns: { jobId }
 */
app.post('/record', function (req, res) {
  try {
    var result = createRecordingJob(req.body || {});
    res.json({ jobId: result.jobId });
  } catch (e) {
    res.status(500).json({ error: 'Could not create recording job: ' + e.message });
  }
});

app.post('/batch-record', batchRecordLimiter, function (req, res) {
  var body = req.body || {};
  var files = Array.isArray(body.files) ? body.files : [];
  if (files.length === 0) {
    return res.status(400).json({ error: 'No HTML files were provided' });
  }

  var batchId = crypto.randomBytes(6).toString('hex');
  var baseOutput = body.output || 'videos.mp4';
  var baseResolved = path.resolve(process.cwd(), baseOutput);
  var baseParsed = path.parse(baseResolved);
  var folderStem = sanitizeFilename(baseParsed.name || 'videos');
  var outputDir = uniqueDirectoryPath(path.join(baseParsed.dir, folderStem + '-batch-' + batchId));

  try {
    fs.mkdirSync(outputDir, { recursive: true });
  } catch (e) {
    return res.status(500).json({ error: 'Could not create output folder: ' + e.message });
  }

  var jobsCreated = [];
  var baseConfig = Object.assign({}, body.config || {});
  delete baseConfig.output;

  try {
    var totalBytes = 0;
    files.forEach(function (file, index) {
      if (!file || !file.htmlContent) {
        throw new Error('File #' + (index + 1) + ' is missing htmlContent');
      }
      totalBytes += Buffer.byteLength(String(file.htmlContent), 'utf8');
      if (totalBytes > maxBatchHtmlBytes) {
        throw new Error('Combined HTML content is too large');
      }
    });
  } catch (e) {
    return res.status(400).json({ error: e.message });
  }

  try {
    files.forEach(function (file, index) {
      var htmlContent = file.htmlContent;
      var rawName = file && file.name ? path.parse(file.name).name : ('file-' + (index + 1));
      var safeName = sanitizeFilename(rawName) || ('file-' + (index + 1));
      var output = uniqueFilePath(path.join(outputDir, safeName + '.mp4'));
      var result = createRecordingJob(Object.assign({}, baseConfig, {
        output: output,
        htmlContent: htmlContent
      }));
      jobsCreated.push({
        jobId: result.jobId,
        name: safeName + '.html',
        output: output
      });
    });
  } catch (e) {
    return res.status(400).json({ error: e.message });
  }

  res.json({
    batchId: batchId,
    outputDirectory: outputDir,
    jobs: jobsCreated
  });
});

/**
 * GET /progress/:jobId
 * Server-Sent Events stream for a running job.
 */
app.get('/progress/:jobId', function (req, res) {
  var job = jobs[req.params.jobId];
  if (!job) {
    return res.status(404).json({ error: 'Job not found' });
  }

  res.setHeader('Content-Type', 'text/event-stream');
  res.setHeader('Cache-Control', 'no-cache');
  res.setHeader('Connection', 'keep-alive');
  res.flushHeaders();

  if (job.status === 'done') {
    res.write('event: done\ndata: ' + JSON.stringify({ output: job.output }) + '\n\n');
    return res.end();
  }
  if (job.status === 'queued') {
    res.write('event: queued\ndata: ' + JSON.stringify({ position: getQueuePosition(job.id) }) + '\n\n');
  }
  if (job.status === 'error') {
    res.write('event: error\ndata: capture failed\n\n');
    return res.end();
  }

  job.subscribers.push(res);

  req.on('close', function () {
    var idx = job.subscribers.indexOf(res);
    if (idx !== -1) { job.subscribers.splice(idx, 1); }
  });
});

/**
 * POST /stop/:jobId
 * Marks a job as stopped (best-effort; ffmpeg process is not forcibly killed).
 */
app.post('/stop/:jobId', function (req, res) {
  var job = jobs[req.params.jobId];
  if (!job) {
    return res.status(404).json({ error: 'Job not found' });
  }
  job.stopped = true;
  if (job.status === 'queued') {
    pendingRecordings = pendingRecordings.filter(function (entry) {
      return entry.job.id !== job.id;
    });
    job.status = 'error';
    job.broadcast('error', 'Job stopped before starting');
    job.subscribers.forEach(function (sub) { try { sub.end(); } catch (e) { /* ignore */ } });
  }
  res.json({ ok: true });
});

/**
 * GET /download/:filename
 * Streams the finished video back to the browser.
 */
app.get('/download/:filename', downloadLimiter, function (req, res) {
  var filename = req.params.filename;
  // Resolve against cwd; reject path traversal attempts
  var resolved = path.resolve(process.cwd(), filename);
  var relative = path.relative(process.cwd(), resolved);
  if (relative.startsWith('..') || path.isAbsolute(relative)) {
    return res.status(400).json({ error: 'Invalid path' });
  }
  if (!fs.existsSync(resolved)) {
    return res.status(404).json({ error: 'File not found' });
  }
  res.download(resolved, path.basename(resolved));
});

var port = parseInt(process.env.PORT || defaultPort, 10);
app.listen(port, function () {
  // eslint-disable-next-line no-console
  console.log('timecut UI running at http://localhost:' + port);
});

module.exports = app;
