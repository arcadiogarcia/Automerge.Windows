// test_changes.cpp — C++ wrapper changes and merge tests
#include <gtest/gtest.h>
#include <automerge/Document.hpp>
#include <automerge/Error.hpp>

using namespace automerge;

// ─── Changes ─────────────────────────────────────────────────────────────────

TEST(Changes, GetAllChanges_NonEmpty) {
    Document doc;
    doc.put_json_root(R"({"a":1})");
    auto changes = doc.get_changes();
    EXPECT_FALSE(changes.empty());
}

TEST(Changes, ApplyToNewDoc_Replicates) {
    Document doc1;
    doc1.put_json_root(R"({"greeting":"hello"})");
    auto changes = doc1.get_changes();

    Document doc2;
    doc2.apply_changes(changes);
    EXPECT_EQ(doc2.get_value(R"(["greeting"])"), R"("hello")");
}

TEST(Changes, IncrementalSinceHeads) {
    Document doc;
    doc.put_json_root(R"({"first":1})");
    auto heads_after_first = doc.get_heads();

    doc.put_json_root(R"({"second":2})");
    auto delta = doc.get_changes(heads_after_first);
    EXPECT_FALSE(delta.empty());
}

TEST(Changes, ApplyInvalid_Throws) {
    Document doc;
    std::vector<uint8_t> garbage{0xDE, 0xAD, 0xBE, 0xEF};
    EXPECT_THROW(doc.apply_changes(garbage), AutomergeError);
}

// ─── Merge ───────────────────────────────────────────────────────────────────

TEST(Merge, CombinesTwoDocs) {
    Document doc1;
    doc1.put_json_root(R"({"a":"from1"})");
    Document doc2;
    doc2.put_json_root(R"({"b":"from2"})");

    doc1.merge(doc2);
    EXPECT_EQ(doc1.get_value(R"(["a"])"), R"("from1")");
    EXPECT_EQ(doc1.get_value(R"(["b"])"), R"("from2")");
}

TEST(Merge, Idempotent) {
    Document doc1;
    doc1.put_json_root(R"({"k":"v"})");
    Document doc2;
    doc2.put_json_root(R"({"k":"v"})");
    EXPECT_NO_THROW(doc1.merge(doc2));
    EXPECT_NO_THROW(doc1.merge(doc2));
}

TEST(Merge, TwoPeersFromSameSnapshot) {
    Document origin;
    origin.put_json_root(R"({"shared":1})");
    auto snapshot = origin.save();

    auto peer1 = Document::load(snapshot);
    auto peer2 = Document::load(snapshot);

    peer1.put_json_root(R"({"p1":"x"})");
    peer2.put_json_root(R"({"p2":"y"})");

    peer1.merge(peer2);
    EXPECT_EQ(peer1.get_value(R"(["p1"])"), R"("x")");
    EXPECT_EQ(peer1.get_value(R"(["p2"])"), R"("y")");
    EXPECT_EQ(peer1.get_value(R"(["shared"])"), "1");
}
