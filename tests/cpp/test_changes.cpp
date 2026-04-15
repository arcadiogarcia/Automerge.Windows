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

// ─── CRDT: Concurrent writes to the same key ─────────────────────────────────

TEST(Merge, ConcurrentKey_BothConvergeToSameValue) {
    Document origin;
    origin.put_json_root(R"({"shared":0})");
    auto snap = origin.save();

    auto doc_a = Document::load(snap);
    auto doc_b = Document::load(snap);
    doc_a.put_json_root(R"({"shared":"from_a"})");
    doc_b.put_json_root(R"({"shared":"from_b"})");

    EXPECT_NO_THROW(doc_a.merge(doc_b));
    EXPECT_NO_THROW(doc_b.merge(doc_a));

    auto val_a = doc_a.get_value(R"(["shared"])");
    auto val_b = doc_b.get_value(R"(["shared"])");
    EXPECT_EQ(val_a, val_b) << "concurrent writes must converge to same value";
    EXPECT_TRUE(val_a == R"("from_a")" || val_a == R"("from_b")")
        << "winner must be one of the two inputs: " << val_a;
    EXPECT_EQ(doc_a.get_heads(), doc_b.get_heads());
}

// ─── Incremental delta excludes prior history ─────────────────────────────────

TEST(Changes, IncrementalDelta_ExcludesPriorHistory) {
    // Build origin in two steps: add "a" first (snapshot), then add "b".
    Document doc;
    doc.put_json_root(R"({"a":1})");
    auto snap_v1  = doc.save();
    auto heads_v1 = doc.get_heads();
    doc.put_json_root(R"({"b":2})");

    // Delta = changes since v1 (only the change that added "b").
    auto delta = doc.get_changes(heads_v1);
    EXPECT_FALSE(delta.empty());

    // Full history must be larger.
    auto full = doc.get_changes();
    EXPECT_LT(delta.size(), full.size());

    // Apply only the delta to a peer pre-loaded with the v1 snapshot so the
    // causal dependency (change for "a") is already satisfied.
    auto peer = Document::load(snap_v1);
    peer.apply_changes(delta);

    EXPECT_EQ(peer.get_value(R"(["a"])"), "1");  // from v1 baseline
    EXPECT_EQ(peer.get_value(R"(["b"])"), "2");  // from delta
}

// ─── API boundary: put_json_root rejects non-scalar values ───────────────────

TEST(DocumentValues, RejectsNestedObjectInPutJsonRoot) {
    Document doc;
    EXPECT_THROW(doc.put_json_root(R"({"nested":{"inner":1}})"), AutomergeError);
}

TEST(DocumentValues, RejectsArrayValueInPutJsonRoot) {
    Document doc;
    EXPECT_THROW(doc.put_json_root(R"({"items":[1,2,3]})"), AutomergeError);
}
