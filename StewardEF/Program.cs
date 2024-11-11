using System.Runtime.InteropServices;
using System.Text;
using Spectre.Console;
using Spectre.Console.Cli;
using StewardEF;
using StewardEF.Commands;

var app = new CommandApp();

if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
{
    Console.OutputEncoding = Encoding.Unicode;
}

app.Configure(config =>
{
    config.SetApplicationName("steward");

    config.AddCommand<SquashMigrationsCommand>("squash")
        .WithDescription("Squashes EF migrations into the first migration.")
        .WithExample(["steward squash", "-d", "path/to/migrations"]);
});

try
{
    app.Run(args);
}
catch (Exception e)
{
    AnsiConsole.WriteException(e, new ExceptionSettings
    {
        Format = ExceptionFormats.ShortenEverything | ExceptionFormats.ShowLinks,
        Style = new ExceptionStyle
        {
            Exception = new Style().Foreground(Color.Grey),
            Message = new Style().Foreground(Color.White),
            NonEmphasized = new Style().Foreground(Color.Cornsilk1),
            Method = new Style().Foreground(Color.Red),
            Path = new Style().Foreground(Color.Red),
        }
    });
}
finally
{
    await VersionChecker.CheckForLatestVersion(); // Ensure the tool is up-to-date
}