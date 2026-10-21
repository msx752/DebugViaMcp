using DebugViaMcp.Vsix.Core.Security;
using Xunit;

namespace DebugViaMcp.Vsix.Core.Tests.Security;

/// <summary>What leaves Visual Studio towards the agent: values that look like secrets are replaced, the rest is untouched.</summary>
public class SensitiveValueMaskTests
{
    private const string Mask = SensitiveValueMask.Placeholder;

    [Theory]
    [InlineData("password")]
    [InlineData("Password")]
    [InlineData("userPassword")]
    [InlineData("_dbPassword")]
    [InlineData("m_apiKey")]
    [InlineData("ApiKey")]
    [InlineData("api_key")]
    [InlineData("clientSecret")]
    [InlineData("accessToken")]
    [InlineData("refreshToken")]
    [InlineData("connectionString")]
    [InlineData("Authorization")]
    [InlineData("privateKey")]
    [InlineData("credentials")]
    [InlineData("config.Password")]
    [InlineData("this.options.SecretKey")]
    public void A_string_under_a_sensitive_name_is_masked(string name)
    {
        var r = SensitiveValueMask.MaskValue(name, "\"hunter2\"");

        Assert.True(r.Masked);
        Assert.Equal(Mask, r.Value);
    }

    [Theory]
    [InlineData(@"password")]
    [InlineData(@"pass\U00000077ord")]
    [InlineData(@"\U00000070assword")]
    public void A_name_written_with_unicode_escapes_is_read_the_way_the_debugger_reads_it(string name)
    {
        var r = SensitiveValueMask.MaskValue(name, "\"hunter2\"");

        Assert.True(r.Masked);
        Assert.Equal(Mask, r.Value);
    }

    [Theory]
    [InlineData(@"pass\UFFFFFFFFord")]
    [InlineData(@"pass\U0000D800ord")]
    public void An_escape_that_is_not_a_character_does_not_break_the_name_check(string name)
    {
        var r = SensitiveValueMask.MaskValue(name, "\"hello\"");

        Assert.False(r.Masked);
    }

    /// <summary>The agent can evaluate an expression built on a secret (hash, comparison, prefix test) and learn it a bit at a time.</summary>
    [Theory]
    [InlineData("password.GetHashCode()", "1234567", "int")]
    [InlineData("password == \"hunter2\"", "true", "bool")]
    [InlineData("password.StartsWith(\"h\")", "true", "bool")]
    [InlineData("password.IndexOf('h')", "0", "int")]
    [InlineData("(int)password[0]", "104", "int")]
    [InlineData("password.Length > 3", "true", "bool")]
    [InlineData("token != null && token.Length == 7", "true", "bool")]
    public void A_scalar_derived_from_a_sensitive_name_is_masked(string expression, string value, string type)
    {
        var r = SensitiveValueMask.MaskValue(expression, value, type);

        Assert.True(r.Masked);
        Assert.Equal(Mask, r.Value);
    }

    [Theory]
    [InlineData("password.Length", "7", "int")]
    [InlineData("credentials.Count", "2", "int")]
    [InlineData("tokenCount", "5", "int")]
    [InlineData("hasPassword", "true", "bool")]
    [InlineData("config.MaxTokens", "4096", "int")]
    public void A_length_a_count_or_a_plain_number_under_a_sensitive_name_stays_readable(string name, string value, string type)
    {
        var r = SensitiveValueMask.MaskValue(name, value, type);

        Assert.False(r.Masked);
        Assert.Equal(value, r.Value);
    }

    /// <summary>A format specifier such as nq removes the quotes; a secret that starts with a brace must not pass for an object.</summary>
    [Theory]
    [InlineData("password", "{hunter2}")]
    [InlineData("password", "null-but-not-null")]
    [InlineData("apiKey", "{abc}")]
    public void A_string_type_decides_before_the_shape_of_the_text(string name, string value)
    {
        var r = SensitiveValueMask.MaskValue(name, value, "string");

        Assert.True(r.Masked);
        Assert.Equal(Mask, r.Value);
    }

    [Fact]
    public void A_null_string_under_a_sensitive_name_is_still_shown_as_null()
    {
        var r = SensitiveValueMask.MaskValue("password", "null", "string");

        Assert.False(r.Masked);
        Assert.Equal("null", r.Value);
    }

    [Theory]
    [InlineData("total", "\"hello\"")]
    [InlineData("orderId", "42")]
    [InlineData("name", "\"Ada\"")]
    [InlineData("Tokenizer", "{Parser.Tokenizer}")]
    [InlineData("passwordLength", "8")]
    [InlineData("hasPassword", "true")]
    [InlineData("password", "")]
    [InlineData("password", "null")]
    public void Other_names_and_values_without_a_secret_to_hide_are_left_alone(string name, string value)
    {
        var r = SensitiveValueMask.MaskValue(name, value);

        Assert.False(r.Masked);
        Assert.Equal(value, r.Value);
    }

    [Theory]
    [InlineData("\"eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.dBjftJeZ4CVPmB92K27uhbUJU1p1r_wW1gFWFOEjXk\"")]
    [InlineData("\"Bearer abcdef0123456789abcdef\"")]
    [InlineData("\"-----BEGIN RSA PRIVATE KEY-----\\nMIIEow...\\n-----END RSA PRIVATE KEY-----\"")]
    [InlineData("\"ghp_0123456789abcdefghijklmnopqrstuvwxyz\"")]
    [InlineData("\"AKIAIOSFODNN7EXAMPLE\"")]
    public void A_value_shaped_like_a_secret_is_masked_whatever_its_name(string value)
    {
        var r = SensitiveValueMask.MaskValue("data", value);

        Assert.True(r.Masked);
        Assert.Equal(Mask, r.Value);
    }

    [Fact]
    public void A_connection_string_keeps_its_shape_and_loses_only_the_secret()
    {
        var r = SensitiveValueMask.MaskValue("text", "\"Server=db;User Id=app;Password=hunter2;Encrypt=true\"");

        Assert.True(r.Masked);
        Assert.Equal($"\"Server=db;User Id=app;Password={Mask};Encrypt=true\"", r.Value);
    }

    [Fact]
    public void A_url_with_a_password_loses_the_password()
    {
        var r = SensitiveValueMask.MaskValue("endpoint", "\"https://app:hunter2@example.com/db\"");

        Assert.True(r.Masked);
        Assert.Equal($"\"https://app:{Mask}@example.com/db\"", r.Value);
    }

    [Fact]
    public void Free_text_is_checked_by_shape_only()
    {
        var text = SensitiveValueMask.MaskText("Login failed. Password=hunter2; Bearer abcdef0123456789abcdef", out var masked);

        Assert.True(masked);
        Assert.Equal($"Login failed. Password={Mask}; {Mask}", text);
    }

    [Fact]
    public void Free_text_without_a_secret_comes_back_unchanged()
    {
        var text = SensitiveValueMask.MaskText("Object reference not set to an instance of an object.", out var masked);

        Assert.False(masked);
        Assert.Equal("Object reference not set to an instance of an object.", text);
    }

    [Fact]
    public void Null_is_handled()
    {
        Assert.Equal("", SensitiveValueMask.MaskValue("password", null).Value);
        Assert.Equal("", SensitiveValueMask.MaskText(null, out var masked));
        Assert.False(masked);
    }

    /// <summary>Ways to read a secret out from under its name without the quotes of a string literal.</summary>
    [Theory]
    [InlineData("password, nq", "hunter2", "string")]
    [InlineData("password", "hunter2", "string")]
    [InlineData("password[0]", "104 'h'", "char")]
    [InlineData("(int)password[0]", "104", "int")]
    [InlineData("pass\\u0077ord", "\"hunter2\"", "string")]
    public void A_secret_read_around_its_quotes_or_name_is_still_masked(string name, string value, string type)
    {
        var r = SensitiveValueMask.MaskValue(name, value, type);

        Assert.True(r.Masked);
        Assert.Equal(Mask, r.Value);
    }

    [Theory]
    [InlineData("password.Length", "8", "int")]
    [InlineData("passwordLength", "8", "int")]
    [InlineData("hasPassword", "true", "bool")]
    [InlineData("credentials", "{Demo.Credentials}", "Demo.Credentials")]
    public void A_number_a_flag_or_an_object_under_a_sensitive_name_is_not_a_secret_to_hide(string name, string value, string type)
    {
        var r = SensitiveValueMask.MaskValue(name, value, type);

        Assert.False(r.Masked);
        Assert.Equal(value, r.Value);
    }

    [Theory]
    [InlineData("password", true)]
    [InlineData("this.options.ApiKey", true)]
    [InlineData("pass\\u0077ord", true)]
    [InlineData("total", false)]
    [InlineData("Tokenizer", false)]
    public void An_expression_names_a_sensitive_value_when_a_word_of_it_does(string expression, bool expected)
        => Assert.Equal(expected, SensitiveValueMask.NamesSensitiveValue(expression));
}
