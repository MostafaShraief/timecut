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
const path = require('path');
const fs = require('fs');
const os = require('os');
const crypto = require('crypto');
const recorder = require('./index.js');

const app = express();
const defaultPort = 3000;
const jobs = {};

app.use(express.json({ limit: '10mb' }));
app.use(express.static(path.join(__dirname, 'public')));

/**
 * POST /record
 * Body: JSON capture config (url | htmlContent, fps, duration, viewport, …)
 * Returns: { jobId }
 */
app.post('/record', function (req, res) {
  var config = req.body || {};
  var jobId = crypto.randomBytes(8).toString('hex');
  var subscribers = [];
  var tmpFile = null;

  // Resolve output relative to cwd
  var output = path.resolve(process.cwd(), config.output || 'video.mp4');
  var captureUrl = config.url;

  // If caller supplied inline HTML, write it to a temp file
  if (config.htmlContent) {
    tmpFile = path.join(os.tmpdir(), 'timecut-' + jobId + '.html');
    try {
      fs.writeFileSync(tmpFile, config.htmlContent, 'utf8');
    } catch (e) {
      return res.status(500).json({ error: 'Could not write temporary HTML file: ' + e.message });
    }
    captureUrl = tmpFile;
  }

  var job = {
    id: jobId,
    status: 'running',
    output: output,
    subscribers: subscribers,
    stopped: false
  };
  jobs[jobId] = job;

  res.json({ jobId: jobId });

  // Broadcast a line to all SSE subscribers
  function broadcast(event, data) {
    var msg = 'event: ' + event + '\ndata: ' + data + '\n\n';
    subscribers.forEach(function (sub) {
      try { sub.write(msg); } catch (e) { /* ignore closed connections */ }
    });
  }

  var fps = parseFloat(config.fps) || 60;
  var duration = parseFloat(config.duration) || 5;
  var totalFrames = Math.round(fps * duration);
  var capturedFrames = 0;

  var captureConfig = Object.assign({}, config, {
    url: captureUrl,
    output: output,
    quiet: true,
    viewport: config.viewport || { width: 800, height: 600 }
  });
  delete captureConfig.htmlContent;

  // Use frameProcessor to track per-frame progress without touching console.log
  captureConfig.pipeMode = false;
  captureConfig.frameProcessor = function () {
    if (!job.stopped) {
      capturedFrames++;
      broadcast('progress', JSON.stringify({ captured: capturedFrames, total: totalFrames }));
    }
  };

  broadcast('log', 'Starting capture: ' + totalFrames + ' frames at ' + fps + ' fps…');

  recorder(captureConfig)
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
  res.json({ ok: true });
});

/**
 * GET /download/:filename
 * Streams the finished video back to the browser.
 */
app.get('/download/:filename', function (req, res) {
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
