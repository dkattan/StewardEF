namespace MigrationSquasher.Features;

using System.Text;
using System.Text.RegularExpressions;
using Spectre.Console;

public static class SquashFeatures
{
    public static void CreateAggregateFiles(string directory)
    {
        var files = Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories);
        var migrationFiles = files
            .Where(f => !f.EndsWith("Designer.cs", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f)
            .ToList();
        if (migrationFiles.Count == 0)
        {
            AnsiConsole.MarkupLine("[red]No migration files found to aggregate.[/]");
            return;
        }

        // Prepare output file paths
        var outputDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "output");
        if (!Directory.Exists(outputDir))
            Directory.CreateDirectory(outputDir);

        var upFilePath = Path.Combine(outputDir, "up.txt");
        var downFilePath = Path.Combine(outputDir, "down.txt");

        // Clear output files if they already exist
        if (File.Exists(upFilePath)) File.Delete(upFilePath);
        if (File.Exists(downFilePath)) File.Delete(downFilePath);

        // Get aggregated Up and Down contents
        var upResult = GetAggregatedMethodContent(migrationFiles, "Up");
        var downResult = GetAggregatedMethodContent(migrationFiles.AsEnumerable().Reverse(), "Down");

        // Write contents to files
        File.WriteAllText(upFilePath, upResult.AggregatedContent);
        File.WriteAllText(downFilePath, downResult.AggregatedContent);

        AnsiConsole.MarkupLine("[green]Aggregation complete![/]");
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

        // Get aggregated Up and Down contents with using statements
        var upResult = GetAggregatedMethodContent(migrationFiles, "Up", collectUsings: true);
        var downResult = GetAggregatedMethodContent(migrationFiles.AsEnumerable().Reverse(), "Down", collectUsings: true);

        // Insert the aggregated content into the first migration file
        var firstMigrationLines = File.ReadAllLines(firstMigrationFile).ToList();

        // Replace the Up and Down methods in the first migration file
        ReplaceMethodContent(firstMigrationLines, "Up", upResult.AggregatedContent);
        ReplaceMethodContent(firstMigrationLines, "Down", downResult.AggregatedContent);

        // Combine using statements from both results
        var allUsingStatements = new HashSet<string>(upResult.UsingStatements);
        foreach (var usingStmt in downResult.UsingStatements)
        {
            allUsingStatements.Add(usingStmt);
        }

        // Update the using statements in the first migration file
        UpdateUsingStatements(firstMigrationLines, allUsingStatements);

        // Write the updated content back to the first migration file
        File.WriteAllLines(firstMigrationFile, firstMigrationLines);

        // Delete subsequent migration files, except the first migration and it's designer file
        var filesToDelete = migrationFiles.Skip(2).ToList();

        foreach (var file in filesToDelete)
        {
            File.Delete(file);
        }

        AnsiConsole.MarkupLine("[green]Migrations squashed successfully![/]");
    }

    
    private class AggregatedMethodResult
    {
        public string AggregatedContent { get; set; }
        public HashSet<string> UsingStatements { get; set; } = [];
    }

    private static AggregatedMethodResult GetAggregatedMethodContent(
        IEnumerable<string> files,
        string methodName,
        bool collectUsings = false)
    {
        var result = new AggregatedMethodResult();
        var aggregatedContent = new StringBuilder();

        foreach (var file in files)
        {
            var fileName = Path.GetFileName(file);
            var migrationLines = File.ReadAllLines(file);

            if (collectUsings)
            {
                // Extract the using statements
                var usings = ExtractUsingStatements(migrationLines);
                foreach (var u in usings)
                {
                    result.UsingStatements.Add(u);
                }
            }

            // Extract the method content
            var methodContent = ExtractMethodContent(migrationLines, methodName);
            if (!string.IsNullOrEmpty(methodContent))
            {
                aggregatedContent.AppendLine($"// {fileName}");
                aggregatedContent.AppendLine(methodContent);
                aggregatedContent.AppendLine();
            }
        }

        result.AggregatedContent = aggregatedContent.ToString();
        return result;
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
    
    private static IEnumerable<string> ExtractUsingStatements(string[] lines)
    {
        return lines.Where(line => line.TrimStart().StartsWith("using ")).Select(line => line.Trim());
    }

    private static void UpdateUsingStatements(List<string> lines, HashSet<string> usingStatements)
    {
        // Find the index of the namespace declaration line
        var namespaceIndex = lines.FindIndex(line => line.TrimStart().StartsWith("namespace "));
        if (namespaceIndex == -1)
        {
            AnsiConsole.MarkupLine("[red]No namespace declaration found in the file.[/]");
            return;
        }

        var insertIndex = namespaceIndex + 1;

        // Remove any existing using statements immediately after the namespace declaration
        while (insertIndex < lines.Count && lines[insertIndex].TrimStart().StartsWith("using "))
        {
            lines.RemoveAt(insertIndex);
        }

        var sortedUsings = usingStatements.OrderBy(u => u).ToList();

        // Insert an empty line after the namespace for readability
        sortedUsings.Insert(0, "");

        // Insert the using statements into the lines at the insertIndex
        lines.InsertRange(insertIndex, sortedUsings);
    }

    private static void ReplaceMethodContent(List<string> lines, string methodName, string newContent)
    {
        var methodSignature = $"protected override void {methodName}(MigrationBuilder migrationBuilder)";
        var methodStartIndex = -1;

        // Find the line index where the method starts
        for (var i = 0; i < lines.Count; i++)
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

        var braceLevel = 0;
        var methodStarted = false;
        var methodEndIndex = -1;

        // Start from the method signature line
        for (var i = methodStartIndex; i < lines.Count; i++)
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
                    methodEndIndex = i;
                    break;
                }
            }
        }

        if (methodEndIndex == -1) return;
        
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