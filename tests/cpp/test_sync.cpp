// test_sync.cpp — C++ wrapper SyncState tests using GoogleTest
#include <gtest/gtest.h>
#include <automerge/Document.hpp>
#include <automerge/SyncState.hpp>
#include <automerge/Error.hpp>

using namespace automerge;

// ─── Helper: run protocol until heads match ────────────────────────────────

static bool sync_until_converged(Document& a, Document& b, int max_rounds = 10) {
    SyncState ss_a, ss_b;
    for (int i = 0; i < max_rounds; ++i) {
        auto msg_ab = ss_a.generate_sync_message(a);
        if (!msg_ab.empty()) ss_b.receive_sync_message(b, msg_ab);

        auto msg_ba = ss_b.generate_sync_message(b);
        if (!msg_ba.empty()) ss_a.receive_sync_message(a, msg_ba);

        auto ha = a.get_heads();
        auto hb = b.get_heads();
        if (ha == hb && !ha.empty()) return true;
    }
    return false;
}

// ─── Lifecycle ────────────────────────────────────────────────────────────────

TEST(SyncStateLifecycle, DefaultConstruct) {
    EXPECT_NO_THROW({ SyncState ss; });
}

TEST(SyncStateLifecycle, MoveConstruct) {
    SyncState ss1;
    SyncState ss2(std::move(ss1));
    (void)ss2;
}

// ─── One-way sync ─────────────────────────────────────────────────────────────

TEST(SyncOneWay, EmptyTargetReceivesData) {
    Document doc_a;
    doc_a.put_json_root(R"({"source":"alice"})");
    Document doc_b;

    bool converged = sync_until_converged(doc_a, doc_b);
    EXPECT_TRUE(converged);
    EXPECT_EQ(doc_b.get_value(R"(["source"])"), R"("alice")");
}

// ─── Bidirectional sync ───────────────────────────────────────────────────────

TEST(SyncBidirectional, BothSeeAllData) {
    Document doc_a;
    doc_a.put_json_root(R"({"by_a":"data_a"})");
    Document doc_b;
    doc_b.put_json_root(R"({"by_b":"data_b"})");

    EXPECT_TRUE(sync_until_converged(doc_a, doc_b));

    EXPECT_EQ(doc_a.get_value(R"(["by_b"])"), R"("data_b")");
    EXPECT_EQ(doc_b.get_value(R"(["by_a"])"), R"("data_a")");
}

TEST(SyncBidirectional, HeadsConverge) {
    Document doc_a;
    doc_a.put_json_root(R"({"a":1})");
    Document doc_b;
    doc_b.put_json_root(R"({"b":2})");

    EXPECT_TRUE(sync_until_converged(doc_a, doc_b));
    EXPECT_EQ(doc_a.get_heads(), doc_b.get_heads());
}

// ─── Multiple peers ───────────────────────────────────────────────────────────

TEST(SyncMultiple, ThreePeersConverge) {
    Document doc_a, doc_b, doc_c;
    doc_a.put_json_root(R"({"writer":"a"})");
    doc_b.put_json_root(R"({"writer_b":"b"})");
    doc_c.put_json_root(R"({"writer_c":"c"})");

    EXPECT_TRUE(sync_until_converged(doc_a, doc_b));
    EXPECT_TRUE(sync_until_converged(doc_a, doc_c));
    EXPECT_TRUE(sync_until_converged(doc_b, doc_c));

    EXPECT_EQ(doc_a.get_value(R"(["writer_b"])"), R"("b")");
    EXPECT_EQ(doc_a.get_value(R"(["writer_c"])"), R"("c")");
}

// ─── Sync state persistence ───────────────────────────────────────────────────

TEST(SyncStatePersistence, SaveLoad) {
    SyncState ss1;
    auto bytes = ss1.save();
    EXPECT_FALSE(bytes.empty());
    auto ss2 = SyncState::load(bytes);
    (void)ss2;
}

// ─── CRDT: Concurrent writes to the same key ─────────────────────────────────

TEST(ConcurrentWrites, SameKeyMerge_BothConvergeToSameValue) {
    // Both peers start from shared snapshot and write the same key with a
    // different value. After bidirectional merge, both must hold the same
    // deterministic CRDT-resolved value.
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

TEST(ConcurrentWrites, SameKeySync_BothConvergeToSameValue) {
    Document origin;
    origin.put_json_root(R"({"counter":0})");
    auto snap = origin.save();

    auto doc_a = Document::load(snap);
    auto doc_b = Document::load(snap);
    doc_a.put_json_root(R"({"counter":100})");
    doc_b.put_json_root(R"({"counter":200})");

    EXPECT_TRUE(sync_until_converged(doc_a, doc_b));

    auto val_a = doc_a.get_value(R"(["counter"])");
    auto val_b = doc_b.get_value(R"(["counter"])");
    EXPECT_EQ(val_a, val_b);
    EXPECT_TRUE(val_a == "100" || val_a == "200")
        << "winner must be one of the inputs: " << val_a;
    EXPECT_EQ(doc_a.get_heads(), doc_b.get_heads());
}

// ─── SyncState resume after save/load ────────────────────────────────────────

TEST(SyncResume, AfterSyncStateSaveLoad) {
    Document doc_a;
    doc_a.put_json_root(R"({"first":"change"})");
    Document doc_b;

    SyncState ss_a, ss_b;

    // Send one message A→B to put states mid-flight.
    auto msg = ss_a.generate_sync_message(doc_a);
    ASSERT_FALSE(msg.empty()) << "first message must be non-empty";
    ss_b.receive_sync_message(doc_b, msg);

    // Persist and reload both sync states.
    auto ss_a2 = SyncState::load(ss_a.save());
    auto ss_b2 = SyncState::load(ss_b.save());

    // Continue sync with the reloaded states.
    bool converged = false;
    for (int i = 0; i < 10; ++i) {
        auto m1 = ss_b2.generate_sync_message(doc_b);
        if (!m1.empty()) ss_a2.receive_sync_message(doc_a, m1);
        auto m2 = ss_a2.generate_sync_message(doc_a);
        if (!m2.empty()) ss_b2.receive_sync_message(doc_b, m2);
        if (doc_a.get_heads() == doc_b.get_heads() && !doc_a.get_heads().empty()) {
            converged = true;
            break;
        }
    }
    EXPECT_TRUE(converged) << "resumed sync must converge";
    EXPECT_EQ(doc_b.get_value(R"(["first"])"), R"("change")");
    EXPECT_EQ(doc_a.get_heads(), doc_b.get_heads());
}

// ─── Sync: shared history then diverge ───────────────────────────────────────

TEST(SyncSharedHistory, BothPeersConverge) {
    Document origin;
    origin.put_json_root(R"({"shared":1})");
    auto snap = origin.save();

    auto doc_a = Document::load(snap);
    auto doc_b = Document::load(snap);
    doc_a.put_json_root(R"({"from_a":"only_in_a"})");
    doc_b.put_json_root(R"({"from_b":"only_in_b"})");

    EXPECT_TRUE(sync_until_converged(doc_a, doc_b));
    EXPECT_EQ(doc_a.get_value(R"(["from_b"])"), R"("only_in_b")");
    EXPECT_EQ(doc_b.get_value(R"(["from_a"])"), R"("only_in_a")");
    EXPECT_EQ(doc_a.get_value(R"(["shared"])"), "1");
    EXPECT_EQ(doc_b.get_value(R"(["shared"])"), "1");
    EXPECT_EQ(doc_a.get_heads(), doc_b.get_heads());
}

// ─── Three peers: all have identical heads after full propagation ─────────────

TEST(SyncMultiple, AllThreePeersHaveIdenticalHeads) {
    Document doc_a, doc_b, doc_c;
    doc_a.put_json_root(R"({"peer":"a"})");
    doc_b.put_json_root(R"({"peer_b":"b"})");
    doc_c.put_json_root(R"({"peer_c":"c"})");

    // A↔B, A↔C, B↔C, then final A↔B so B picks up C's data via A.
    EXPECT_TRUE(sync_until_converged(doc_a, doc_b));
    EXPECT_TRUE(sync_until_converged(doc_a, doc_c));
    EXPECT_TRUE(sync_until_converged(doc_b, doc_c));
    EXPECT_TRUE(sync_until_converged(doc_a, doc_b));

    EXPECT_EQ(doc_a.get_heads(), doc_b.get_heads()) << "A and B heads must match";
    EXPECT_EQ(doc_b.get_heads(), doc_c.get_heads()) << "B and C heads must match";

    EXPECT_EQ(doc_c.get_value(R"(["peer"])"),   R"("a")");
    EXPECT_EQ(doc_a.get_value(R"(["peer_b"])"), R"("b")");
    EXPECT_EQ(doc_b.get_value(R"(["peer_c"])"), R"("c")");
}

// ─── Stress: 50-change sync ───────────────────────────────────────────────────

TEST(SyncStress, FiftyChangesConverge) {
    Document doc_a, doc_b;
    for (int i = 0; i < 50; ++i) {
        std::string json = "{\"key_" + std::to_string(i) + "\":" + std::to_string(i) + "}";
        doc_a.put_json_root(json);
    }

    EXPECT_TRUE(sync_until_converged(doc_a, doc_b, 50));
    EXPECT_EQ(doc_b.get_value(R"(["key_0"])"),  "0");
    EXPECT_EQ(doc_b.get_value(R"(["key_49"])"), "49");
    EXPECT_EQ(doc_a.get_heads(), doc_b.get_heads());
}
