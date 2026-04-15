using System;
using System.Text.Json;
using Automerge.Windows;
using Xunit;

namespace AutomergeTests;

/// <summary>Tests for Document lifecycle, persistence, heads, and values.</summary>
public class DocumentTests
{
    // ─── Lifecycle ────────────────────────────────────────────────────────────

    [Fact]
    public void CreateAndDispose_DoesNotThrow()
    {
        using var doc = new Document();
        Assert.NotNull(doc);
    }

    [Fact]
    public void MultipleCreateDispose_DoesNotThrow()
    {
        for (int i = 0; i < 10; i++)
        {
            using var doc = new Document();
        }
    }

    [Fact]
    public void DoubleDispose_DoesNotThrow()
    {
        var doc = new Document();
        doc.Dispose();
        doc.Dispose(); // second dispose must be safe
    }

    // ─── Persistence ──────────────────────────────────────────────────────────

    [Fact]
    public void Save_ReturnsNonEmptyBytes()
    {
        using var doc = new Document();
        var bytes = doc.Save();
        Assert.NotEmpty(bytes);
    }

    [Fact]
    public void SaveLoad_RoundtripPreservesData()
    {
        using var doc1 = new Document();
        doc1.PutJsonRoot("""{"name":"Alice","age":30}""");
        var bytes = doc1.Save();

        using var doc2 = Document.Load(bytes);
        var name = doc2.GetValue("""["name"]""");
        Assert.Equal("\"Alice\"", name);
        var age = doc2.GetValue("""["age"]""");
        Assert.Equal("30", age);
    }

    [Fact]
    public void Load_InvalidBytes_ThrowsException()
    {
        var garbage = "not valid automerge data"u8.ToArray();
        Assert.Throws<AutomergeNativeException>(() => Document.Load(garbage));
    }

    // ─── Heads ────────────────────────────────────────────────────────────────

    [Fact]
    public void GetHeads_EmptyDoc_ReturnsMultipleOf32()
    {
        using var doc = new Document();
        var heads = doc.GetHeads();
        Assert.Equal(0, heads.Length % 32);
    }

    [Fact]
    public void GetHeads_AfterPut_Changed()
    {
        using var doc = new Document();
        var h0 = doc.GetHeads();
        doc.PutJsonRoot("""{"k":"v"}""");
        var h1 = doc.GetHeads();
        Assert.True(h1.Length >= 32);
        Assert.NotEqual(h0, h1);
    }

    [Fact]
    public void GetHeads_AlwaysMultipleOf32()
    {
        using var doc = new Document();
        doc.PutJsonRoot("""{"x":1}""");
        var heads = doc.GetHeads();
        Assert.Equal(0, heads.Length % 32);
    }

    // ─── Values ───────────────────────────────────────────────────────────────

    [Fact]
    public void GetValue_EmptyPath_ReturnsRootJson()
    {
        using var doc = new Document();
        doc.PutJsonRoot("""{"score":99,"label":"test"}""");
        var root = doc.GetValue("[]");
        var parsed = JsonSerializer.Deserialize<JsonElement>(root);
        Assert.Equal(99, parsed.GetProperty("score").GetInt32());
        Assert.Equal("test", parsed.GetProperty("label").GetString());
    }

    [Fact]
    public void GetValue_KeyPath_ReturnsScalar()
    {
        using var doc = new Document();
        doc.PutJsonRoot("""{"greeting":"hello"}""");
        var val = doc.GetValue("""["greeting"]""");
        Assert.Equal("\"hello\"", val);
    }

    [Fact]
    public void GetValue_MissingKey_ThrowsException()
    {
        using var doc = new Document();
        doc.PutJsonRoot("""{"real":1}""");
        Assert.Throws<AutomergeNativeException>(() => doc.GetValue("""["missing"]"""));
    }

    [Fact]
    public void MultipleScalarTypes_RoundtripCorrectly()
    {
        using var doc = new Document();
        doc.PutJsonRoot("""{"s":"text","i":-5,"b":true,"n":null}""");
        Assert.Equal("\"text\"", doc.GetValue("""["s"]"""));
        Assert.Equal("-5", doc.GetValue("""["i"]"""));
        Assert.Equal("true", doc.GetValue("""["b"]"""));
        Assert.Equal("null", doc.GetValue("""["n"]"""));
    }

    [Fact]
    public void OverwriteValue_ReturnsLatest()
    {
        using var doc = new Document();
        doc.PutJsonRoot("""{"counter":1}""");
        doc.PutJsonRoot("""{"counter":2}""");
        doc.PutJsonRoot("""{"counter":3}""");
        Assert.Equal("3", doc.GetValue("""["counter"]"""));
    }
}
