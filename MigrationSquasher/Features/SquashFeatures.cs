namespace MigrationSquasher.Features;

using System.Text;
using System.Text.RegularExpressions;
using Spectre.Console;

public static class SquashFeatures
{
    public static void CreateAggregateFiles(string directory)
    {
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
    }

    private static string ExtractMethodContent(string[] lines, string methodName)
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
    
    public static void SquashMigrations(string directory)
    {
        // Get all .cs files including Designer.cs files
        var files = Directory.GetFiles(directory, "*.cs", SearchOption.TopDirectoryOnly)
            .OrderBy(f => f)
            .ToList();

        // Filter out the snapshot files
        var migrationFiles = files
            .Where(f => !Path.GetFileName(f).EndsWith("ModelSnapshot.cs", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f)
            .ToList();

        if (migrationFiles.Count == 0)
        {
            AnsiConsole.MarkupLine("[red]No migration files found to squash.[/]");
            return;
        }

        // Get the first migration file
        var firstMigrationFile = migrationFiles.First();

        // Prepare to collect aggregated Up and Down contents
        var aggregatedUpContent = new StringBuilder();
        var aggregatedDownContent = new StringBuilder();
        var usingStatements = new HashSet<string>();

        // Collect Up and Down contents and using statements from all migration files
        foreach (var file in migrationFiles)
        {
            var fileName = Path.GetFileName(file);
            var migrationLines = File.ReadAllLines(file);

            // Extract the using statements
            var usings = ExtractUsingStatements(migrationLines);
            foreach (var u in usings)
            {
                usingStatements.Add(u);
            }

            // Extract the contents of the Up and Down methods
            var upContent = ExtractMethodContent(migrationLines, "Up");
            var downContent = ExtractMethodContent(migrationLines, "Down");

            if (!string.IsNullOrEmpty(upContent))
            {
                aggregatedUpContent.AppendLine($"// {fileName}");
                aggregatedUpContent.AppendLine(upContent);
                aggregatedUpContent.AppendLine();
            }

            if (!string.IsNullOrEmpty(downContent))
            {
                aggregatedDownContent.AppendLine($"// {fileName}");
                aggregatedDownContent.AppendLine(downContent);
                aggregatedDownContent.AppendLine();
            }
        }

        // Insert the aggregated content into the first migration file
        var firstMigrationLines = File.ReadAllLines(firstMigrationFile).ToList();

        // Replace the Up and Down methods in the first migration file
        ReplaceMethodContent(firstMigrationLines, "Up", aggregatedUpContent.ToString());
        ReplaceMethodContent(firstMigrationLines, "Down", aggregatedDownContent.ToString());

        // Update the using statements in the first migration file
        UpdateUsingStatements(firstMigrationLines, usingStatements);

        // Write the updated content back to the first migration file
        File.WriteAllLines(firstMigrationFile, firstMigrationLines);

        // Delete subsequent migration files and their designer files
        var filesToDelete = migrationFiles.Skip(2).ToList();

        foreach (var file in filesToDelete)
        {
            File.Delete(file);
        }

        AnsiConsole.MarkupLine("[green]Migrations squashed successfully![/]");
    }

    private static IEnumerable<string> ExtractUsingStatements(string[] lines)
    {
        return lines.Where(line => line.TrimStart().StartsWith("using ")).Select(line => line.Trim());
    }

    private static void UpdateUsingStatements(List<string> lines, HashSet<string> usingStatements)
    {
        // Find the index of the namespace declaration
        int namespaceIndex = -1;
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].TrimStart().StartsWith("namespace "))
            {
                namespaceIndex = i;
                break;
            }
        }

        if (namespaceIndex == -1)
        {
            // No namespace found, maybe an error
            AnsiConsole.MarkupLine("[red]No namespace declaration found in the first migration file.[/]");
            return;
        }

        // Collect all lines before the namespace
        var headerLines = lines.Take(namespaceIndex).ToList();

        // Remove existing using statements from headerLines
        headerLines = headerLines.Where(line => !line.TrimStart().StartsWith("using ")).ToList();

        // Prepare the sorted using statements
        var sortedUsings = usingStatements.OrderBy(u => u).ToList();

        // Build the new header
        var newHeaderLines = new List<string>();
        newHeaderLines.AddRange(headerLines);
        newHeaderLines.AddRange(sortedUsings);
        newHeaderLines.Add(""); // Add an empty line

        // Replace the lines in the file
        lines.RemoveRange(0, namespaceIndex);
        lines.InsertRange(0, newHeaderLines);
    }

    private static void ReplaceMethodContent(List<string> lines, string methodName, string newContent)
    {
        var methodSignature = $"protected override void {methodName}(MigrationBuilder migrationBuilder)";
        int methodStartIndex = -1;

        // Find the line index where the method starts
        for (int i = 0; i < lines.Count; i++)
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
            return;
        }

        int braceLevel = 0;
        bool methodStarted = false;
        int methodEndIndex = -1;

        // Start from the method signature line
        for (int i = methodStartIndex; i < lines.Count; i++)
        {
            var line = lines[i];

            if (!methodStarted)
            {
                // Look for the opening brace
                if (line.Contains("{"))
                {
                    methodStarted = true;
                    braceLevel += line.Count(c => c == '{') - line.Count(c => c == '}');
                }
            }
            else
            {
                braceLevel += line.Count(c => c == '{') - line.Count(c => c == '}');

                if (braceLevel == 0)
                {
                    // End of method
                    methodEndIndex = i;
                    break;
                }
            }
        }

        if (methodEndIndex != -1)
        {
            // Replace the method content
            var newMethodContent = new List<string>();

            // Add method signature and opening brace
            newMethodContent.Add(lines[methodStartIndex]);

            // Ensure opening brace is on its own line
            if (!lines[methodStartIndex].Trim().EndsWith("{"))
            {
                newMethodContent.Add("{");
            }

            // Add the new content with proper indentation
            var indentation = GetIndentation(lines[methodStartIndex]);
            var indentedContent = IndentContent(newContent, indentation + "    ");
            newMethodContent.AddRange(indentedContent);

            // Add closing brace
            newMethodContent.Add(indentation + "}");

            // Replace the old method lines with the new ones
            lines.RemoveRange(methodStartIndex, methodEndIndex - methodStartIndex + 1);
            lines.InsertRange(methodStartIndex, newMethodContent);
        }
    }

    private static string GetIndentation(string line)
    {
        var match = Regex.Match(line, @"^\s*");
        return match.Value;
    }

    private static List<string> IndentContent(string content, string indentation)
    {
        var lines = content.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
        var indentedLines = lines.Select(line => indentation + line).ToList();
        return indentedLines;
    }

}