namespace Timecut.App;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var window = new Window(new MainPage())
		{
			Title = "Timecut",
			Width = 1400,
			Height = 900,
			MinimumWidth = 1000,
			MinimumHeight = 600
		};
		return window;
	}
}
