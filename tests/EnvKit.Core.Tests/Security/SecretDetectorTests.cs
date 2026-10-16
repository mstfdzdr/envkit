using EnvKit.Core.Security;

namespace EnvKit.Core.Tests.Security;

public class SecretDetectorTests
{
    [Theory]
    [InlineData("GITHUB_TOKEN", "x")]
    [InlineData("AZDO_PAT", "x")]
    [InlineData("SPARK_API_KEY", "x")]
    [InlineData("OPENAI_APIKEY", "x")]
    [InlineData("DB_PASSWORD", "x")]
    [InlineData("ConnectionString", "x")]
    [InlineData("AWS_SECRET_ACCESS_KEY", "x")]
    [InlineData("NUGET_AUTH", "x")]
    [InlineData("WHATEVER", "ghp_abc")]
    [InlineData("WHATEVER", "sk-abc")]
    public void Flags_credentials_by_name_or_value(string name, string value) =>
        Assert.True(SecretDetector.LooksSecret(name, value));

    [Theory]
    [InlineData("Path", @"C:\Windows")]
    [InlineData("JAVA_HOME", @"C:\jdk")]
    [InlineData("NVM_HOME", @"C:\nvm")]
    [InlineData("TEMP", @"%USERPROFILE%\AppData\Local\Temp")]
    [InlineData("SPARK_BASE_URL", "http://localhost:8090")]
    [InlineData("NUMBER_OF_PROCESSORS", "16")]
    [InlineData("GIT_AUTHOR_NAME", "me")]
    public void Leaves_ordinary_variables_alone(string name, string value) =>
        Assert.False(SecretDetector.LooksSecret(name, value));
}
