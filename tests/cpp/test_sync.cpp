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
