// test_document.cpp — C++ wrapper Document tests using GoogleTest
#include <gtest/gtest.h>
#include <automerge/Document.hpp>
#include <automerge/Error.hpp>

using namespace automerge;

// ─── Lifecycle ────────────────────────────────────────────────────────────────

TEST(DocumentLifecycle, CreateDestroy) {
    EXPECT_NO_THROW({ Document doc; });
}

TEST(DocumentLifecycle, MultipleCreate) {
    for (int i = 0; i < 10; ++i) {
        Document doc;
        (void)doc;
    }
}

TEST(DocumentLifecycle, MoveConstruct) {
    Document doc1;
    doc1.put_json_root(R"({"k":"v"})");
    Document doc2(std::move(doc1));
    EXPECT_EQ(doc2.get_value("[\"k\"]"), R"("v")");
}

TEST(DocumentLifecycle, MoveAssign) {
    Document doc1;
    doc1.put_json_root(R"({"k":"v"})");
    Document doc2;
    doc2 = std::move(doc1);
    EXPECT_EQ(doc2.get_value("[\"k\"]"), R"("v")");
}

// ─── Persistence ─────────────────────────────────────────────────────────────

TEST(DocumentPersistence, SaveReturnsNonEmpty) {
    Document doc;
    auto bytes = doc.save();
    EXPECT_FALSE(bytes.empty());
}

TEST(DocumentPersistence, SaveLoadRoundtrip) {
    Document doc1;
    doc1.put_json_root(R"({"name":"Alice","score":42})");
    auto bytes = doc1.save();

    auto doc2 = Document::load(bytes);
    EXPECT_EQ(doc2.get_value(R"(["name"])"),   R"("Alice")");
    EXPECT_EQ(doc2.get_value(R"(["score"])"),  "42");
}

TEST(DocumentPersistence, LoadInvalidBytes_Throws) {
    std::vector<uint8_t> garbage{1, 2, 3, 4, 5};
    EXPECT_THROW(Document::load(garbage), AutomergeError);
}

// ─── Heads ───────────────────────────────────────────────────────────────────

TEST(DocumentHeads, EmptyDoc_MultipleOf32) {
    Document doc;
    auto h = doc.get_heads();
    EXPECT_EQ(h.size() % 32, 0u);
}

TEST(DocumentHeads, ChangesAfterPut) {
    Document doc;
    auto h0 = doc.get_heads();
    doc.put_json_root(R"({"x":1})");
    auto h1 = doc.get_heads();
    EXPECT_GE(h1.size(), 32u);
    EXPECT_NE(h0, h1);
}

TEST(DocumentHeads, AlwaysMultipleOf32) {
    Document doc;
    doc.put_json_root(R"({"x":1})");
    auto h = doc.get_heads();
    EXPECT_EQ(h.size() % 32, 0u);
}

// ─── get_value ────────────────────────────────────────────────────────────────

TEST(DocumentValues, RootSerialisesToJson) {
    Document doc;
    doc.put_json_root(R"({"greeting":"hello","n":42})");
    auto root = doc.get_value("[]");
    EXPECT_NE(root.find("hello"), std::string::npos);
    EXPECT_NE(root.find("42"), std::string::npos);
}

TEST(DocumentValues, KeyPathReturnsScalar) {
    Document doc;
    doc.put_json_root(R"({"greeting":"hello"})");
    EXPECT_EQ(doc.get_value(R"(["greeting"])"), R"("hello")");
}

TEST(DocumentValues, MissingKey_Throws) {
    Document doc;
    doc.put_json_root(R"({"real":1})");
    EXPECT_THROW(doc.get_value(R"(["missing"])"), AutomergeError);
}

TEST(DocumentValues, VariousScalars) {
    Document doc;
    doc.put_json_root(R"({"s":"txt","i":-5,"b":true,"n":null})");
    EXPECT_EQ(doc.get_value(R"(["s"])"), R"("txt")");
    EXPECT_EQ(doc.get_value(R"(["i"])"), "-5");
    EXPECT_EQ(doc.get_value(R"(["b"])"), "true");
    EXPECT_EQ(doc.get_value(R"(["n"])"), "null");
}

TEST(DocumentValues, OverwriteReturnsLatest) {
    Document doc;
    doc.put_json_root(R"({"c":1})");
    doc.put_json_root(R"({"c":2})");
    doc.put_json_root(R"({"c":3})");
    EXPECT_EQ(doc.get_value(R"(["c"])"), "3");
}