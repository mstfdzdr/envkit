using System.Text;
using System.Text.Json;
using EnvKit.Core.DotEnv;

namespace EnvKit.Core.Tests.DotEnv;

public class UserSecretsDocumentTests
{
    private static UserSecretsDocument Doc(string json) => UserSecretsDocument.Parse(Encoding.UTF8.GetBytes(json));

    [Fact]
    public void Flattens_nested_and_flat_keys_like_configuration_does()
    {
        var doc = Doc("""
            {
              // comments are allowed
              "ConnectionStrings:Default": "Server=.",
              "Stripe": { "ApiKey": "sk_test", "Retries": 3 },
              "Hosts": [ "a", "b" ],
            }
            """);

        Assert.Equal(
            [
                new("ConnectionStrings:Default", "Server=."),
                new("Stripe:ApiKey", "sk_test"),
                new("Stripe:Retries", "3"),
                new("Hosts:0", "a"),
                new("Hosts:1", "b"),
            ],
            doc.Values());
        Assert.Equal("sk_test", doc.Get("stripe:apikey"));
    }

    [Fact]
    public void Edits_keep_the_shape_a_key_already_has()
    {
        var doc = Doc("""{ "Flat:Key": "1", "Nested": { "Key": "2" } }""");

        doc.Set("flat:key", "10");
        doc.Set("Nested:Key", "20");
        doc.Set("New:Key", "30");

        var json = JsonDocument.Parse(doc.ToBytes()).RootElement;
        Assert.Equal("10", json.GetProperty("Flat:Key").GetString());
        Assert.Equal("20", json.GetProperty("Nested").GetProperty("Key").GetString());
        Assert.Equal("30", json.GetProperty("New:Key").GetString());
    }

    [Fact]
    public void Remove_finds_flat_and_nested_keys()
    {
        var doc = Doc("""{ "A:B": "1", "C": { "D": "2" } }""");

        Assert.True(doc.Remove("a:b"));
        Assert.True(doc.Remove("C:D"));
        Assert.False(doc.Remove("C:D"));
        Assert.Empty(doc.Values());
    }

    [Fact]
    public void Missing_file_is_an_empty_document()
    {
        var doc = UserSecretsDocument.Parse(null);
        doc.Set("K", "v");

        Assert.Equal("v", Doc(Encoding.UTF8.GetString(doc.ToBytes())).Get("K"));
    }

    [Theory]
    [InlineData("[1]")]
    [InlineData("not json")]
    public void Non_objects_are_rejected(string json) => Assert.ThrowsAny<JsonException>(() => Doc(json));
}
