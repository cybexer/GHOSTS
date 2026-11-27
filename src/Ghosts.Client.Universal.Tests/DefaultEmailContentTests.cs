using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Mail;
using System.Text.Json;
using System.Text.RegularExpressions;
using FileHelpers;
using Ghosts.Client.Universal.Infrastructure.Email;
using Xunit;

namespace Ghosts.Client.Universal.Tests;

public class DefaultEmailContentTests
{
    private static readonly string ConfigDirectory = FindConfigDirectory();

    private static string FindConfigDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "Ghosts.Client.Universal", "config");
            if (File.Exists(Path.Combine(candidate, "email-content.csv")))
                return candidate;
        }
        throw new DirectoryNotFoundException("Ghosts.Client.Universal/config not found");
    }

    private static string ConfigFile(string name) => Path.Combine(ConfigDirectory, name);

    private static EmailContent[] ReadContent() => new FileHelperEngine<EmailContent>().ReadFile(ConfigFile("email-content.csv"));

    [Fact]
    public void EmailContentRowsAreCompleteAndUnique()
    {
        var rows = ReadContent();

        Assert.NotEmpty(rows);
        Assert.All(rows, row =>
        {
            Assert.False(string.IsNullOrWhiteSpace(row.Id));
            Assert.False(string.IsNullOrWhiteSpace(row.Subject));
            Assert.False(string.IsNullOrWhiteSpace(row.Body));
            var lines = row.Body.Split("\\n");
            Assert.True(lines.Length >= 4, row.Id);
            Assert.StartsWith("From:", lines[0]);
            Assert.StartsWith("Sent:", lines[1]);
            Assert.StartsWith("To:", lines[2]);
        });
        Assert.Equal(rows.Length, rows.Select(row => row.Id).Distinct().Count());
    }

    [Fact]
    public void EmailContentTokensHaveConfiguredValues()
    {
        using var application = JsonDocument.Parse(File.ReadAllText(ConfigFile("application.json")));
        var values = application.RootElement.GetProperty("EmailContent").EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.GetString());

        var tokens = ReadContent()
            .SelectMany(row => Regex.Matches(row.Subject + row.Body, @"<(\w+)/>"))
            .Select(match => match.Groups[1].Value)
            .Distinct();

        Assert.All(tokens, token => Assert.False(string.IsNullOrEmpty(values.GetValueOrDefault(token)), token));
    }

    [Fact]
    public void EmailReplyLinesAreNonEmptySingleFields()
    {
        var lines = File.ReadAllLines(ConfigFile("email-reply.csv"));

        Assert.NotEmpty(lines);
        Assert.All(lines, line =>
        {
            Assert.False(string.IsNullOrWhiteSpace(line));
            Assert.DoesNotContain("|", line);
        });
    }

    [Theory]
    [InlineData("emails-domain.json")]
    [InlineData("emails-outside.json")]
    public void EmailAddressListsContainValidAddresses(string file)
    {
        var addresses = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(ConfigFile(file)));

        Assert.NotEmpty(addresses!);
        Assert.All(addresses!, address => Assert.True(MailAddress.TryCreate(address, out var parsed) && parsed.Address == address, address));
    }

    [Fact]
    public void EmailFooterUsesOnlySupportedPlaceholders()
    {
        var placeholders = Regex.Matches(File.ReadAllText(ConfigFile("emails-footer.txt")), @"\{\{(\w+)\}\}")
            .Select(match => match.Groups[1].Value);

        Assert.All(placeholders, placeholder => Assert.Contains(placeholder, new[] { "from", "now", "id" }));
    }
}
