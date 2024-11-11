
// Prompt for the input directory

using System.Text;
using System.Text.RegularExpressions;
using Spectre.Console;

var directory = AnsiConsole.Ask<string>("[green]Enter the input directory that houses the EF migrations:[/]");

// Verify the directory exists
if (!Directory.Exists(directory))
{
    AnsiConsole.MarkupLine("[red]The specified directory does not exist.[/]");
    return;
}

// Get all .cs files excluding Designer.cs files
var files = Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories);
var migrationFiles = files.Where(f => !f.EndsWith("Designer.cs", StringComparison.OrdinalIgnoreCase))
    .OrderBy(f => f)
    .ToList();

// Prepare output file paths
var outputDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "output");
if (!Directory.Exists(outputDir))
    Directory.CreateDirectory(outputDir);

var upFilePath = Path.Combine(outputDir, "up.txt");
var downFilePath = Path.Combine(outputDir, "down.txt");

// Clear output files if they already exist
if (File.Exists(upFilePath)) File.Delete(upFilePath);
if (File.Exists(downFilePath)) File.Delete(downFilePath);

// Process each migration file
foreach (var file in migrationFiles)
{
    var fileName = Path.GetFileName(file);
    var migrationLines = File.ReadAllLines(file);

    // Extract the contents of the Up and Down methods
    var upContent = ExtractMethodContent(migrationLines, "Up");
    var downContent = ExtractMethodContent(migrationLines, "Down");

    // Append to up.txt
    if (!string.IsNullOrEmpty(upContent))
    {
        File.AppendAllText(upFilePath, $"// {fileName}{Environment.NewLine}{upContent}{Environment.NewLine}{Environment.NewLine}");
    }

    // Append to down.txt
    if (!string.IsNullOrEmpty(downContent))
    {
        File.AppendAllText(downFilePath, $"// {fileName}{Environment.NewLine}{downContent}{Environment.NewLine}{Environment.NewLine}");
    }
}

AnsiConsole.MarkupLine("[green]Aggregation complete![/]");

static string ExtractMethodContent(string[] lines, string methodName)
    {
        var methodSignature = $"protected override void {methodName}(MigrationBuilder migrationBuilder)";
        int methodStartIndex = -1;

        // Find the line index where the method starts
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains(methodSignature))
            {
                methodStartIndex = i;
                break;
            }
        }

        if (methodStartIndex == -1)
        {
            // Method not found
            return string.Empty;
        }

        var methodContent = new StringBuilder();
        int braceLevel = 0;
        bool methodStarted = false;

        // Start from the method signature line
        for (int i = methodStartIndex; i < lines.Length; i++)
        {
            var line = lines[i];

            if (!methodStarted)
            {
                // Look for the opening brace
                if (line.Contains("{"))
                {
                    methodStarted = true;
                    braceLevel += line.Count(c => c == '{') - line.Count(c => c == '}');

                    // Capture the content after the opening brace on the same line
                    int braceIndex = line.IndexOf('{');
                    if (braceIndex + 1 < line.Length)
                    {
                        var afterBrace = line.Substring(braceIndex + 1);
                        if (!string.IsNullOrWhiteSpace(afterBrace))
                        {
                            methodContent.AppendLine(afterBrace);
                        }
                    }
                }
            }
            else
            {
                braceLevel += line.Count(c => c == '{') - line.Count(c => c == '}');

                if (braceLevel == 0)
                {
                    // End of method
                    break;
                }
                else
                {
                    methodContent.AppendLine(line);
                }
            }
        }

        return methodContent.ToString().Trim();
    }
