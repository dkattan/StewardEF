using MigrationSquasher;
using Spectre.Console;
using static MigrationSquasher.Features.SquashFeatures;

var directory = AnsiConsole.Ask<string>("[green]Enter the input directory that houses the EF migrations:[/]");

if (!Directory.Exists(directory))
{
    AnsiConsole.MarkupLine("[red]The specified directory does not exist.[/]");
    return;
}

var choice = AnsiConsole.Prompt(
    new SelectionPrompt<string>()
        .Title("What would you like to do?")
        .AddChoices([ActionChoices.CreateAggregateFiles, ActionChoices.SquashMigrations]));

switch (choice)
{
    case ActionChoices.CreateAggregateFiles:
        CreateAggregateFiles(directory);
        break;
    case ActionChoices.SquashMigrations:
        SquashMigrations(directory);
        break;
}