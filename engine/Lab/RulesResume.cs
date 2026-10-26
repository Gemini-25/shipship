using System.Text.Json.Nodes;
using ShipEngine;
using ShipView;

namespace Lab;

/// <summary>
/// 이어 보기 규칙 (docs/OBSERVER.md §16 S6 · S7 — 묶음 '이어 보기'): 최근 셋을 돌려 쓴다 · 쓰다 멈춘 임시 파일은 읽지 않는다 · 가장 새것이 깨졌으면 그다음 ·
/// 판이 다르면 세계를 읽지 않고 '이어 볼 수 없다' · 모르는 마디는 건너뛰고 없는 마디는 빈 채로.
/// </summary>
public static partial class Program
{
    private static void ResumeRules()
    {
        if (!Do("이어 보기")) return;
        Console.WriteLine("\n[이어 보기 — 돌려 쓰는 저장 셋 · 판 · 마디]");
        string dir = TempVoyages();
        try
        {
            if (Environment.GetEnvironmentVariable("SAVE_BENCH") is { Length: > 0 } ship)
            {
                var big = LiveShow.Crewed(ship, 5); big.FastForward(60);
                for (int k = 0; k < 4; k++)
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew(); var bb = big.SaveBundle(SaveKind.Daily); double ms = sw.Elapsed.TotalMilliseconds;
                    sw.Restart(); var bytes = bb.ToBytes(); double packMs = sw.Elapsed.TotalMilliseconds;
                    sw.Restart(); var back = LiveShow.Resume(VoyageBundle.FromBytes(bytes, out _)!); double resMs = sw.Elapsed.TotalMilliseconds;
                    Console.WriteLine($"    (저장 재기) {ship} 묶음 짓기 {ms:0.0}ms · 세계 {bb.World.Length / 1e6:0.00}MB · 연출 {bb.Record!.Length / 1e3:0}KB (미리 보기 세계 {(big.Dir.Ahead != null ? "있음" : "없음")}) · 손 {bb.Hands!.Length}B · 화면 {bb.Screen!.Length / 1e3:0}KB · 압축 {packMs:0}ms → {bytes.Length / 1e6:0.00}MB · 이어 보기 {resMs:0}ms");
                    big.FastForward(20);
                }
            }
            var show = new LiveShow(HandShip(53), "디딤호");
            var store = new VoyageStore(dir);
            // S6: 저장 넷 → 셋이 남고 가장 오래된 것이 지워진다 · 쓰다 멈춘 임시 파일은 읽지 않는다 · 가장 새것이 깨졌으면 그다음
            {
                var ticks = new List<long>(); VoyageMeta? meta = null;
                for (int i = 0; i < 4; i++) { show.FastForward(3); meta = store.Write(show.SaveBundle(SaveKind.Daily)); ticks.Add(show.Sim.Tick); }
                string folder = store.FolderOf(meta!);
                var files = Directory.Exists(folder) ? Directory.GetFiles(folder, "save-*.bin").Select(Path.GetFileName).OrderBy(x => x).ToList() : new();
                var read = store.Read(meta!).Select(x => x.bundle?.Meta.Tick ?? -1).ToList();
                var latest = store.Latest();
                bool three = files.Count == 3 && read.SequenceEqual(new[] { ticks[3], ticks[2], ticks[1] }) && latest?.Tick == ticks[3];
                // 쓰다 멈춘 임시 파일 (다음 칸 이름에 반쯤 쓴 바이트) — 읽지 않는다
                string next = store.Rotate(meta!);
                var half = show.SaveBundle(SaveKind.Daily).ToBytes();
                if (Directory.Exists(folder)) File.WriteAllBytes(Path.Combine(folder, $"save-{next}.bin.tmp"), half.AsSpan(0, half.Length / 2).ToArray());
                var r1 = latest != null ? LiveShow.Resume(store, latest, out _) : null;
                // 가장 새것을 깨뜨린다 (끝을 자른다) — 그다음 것으로 이어 본다
                string newest = latest != null ? Path.Combine(folder, $"save-{latest.Slot}.bin") : "";
                if (File.Exists(newest)) { var raw = File.ReadAllBytes(newest); File.WriteAllBytes(newest, raw.AsSpan(0, raw.Length * 2 / 3).ToArray()); }
                var r2 = latest != null ? LiveShow.Resume(store, latest, out _) : null;
                Check("이어 보기 S6: 저장 넷 → 돌려 쓰는 셋이 남고 가장 오래된 것이 지워진다 · 쓰다 멈춘 임시 파일은 읽지 않는다 · 가장 새것이 깨졌으면 그다음 것으로 이어 본다",
                    three && r1?.Sim.Tick == ticks[3] && r2?.Sim.Tick == ticks[2],
                    $"남은 파일 {string.Join(" ", files)} · 읽은 차례 {string.Join(" ", read)} (쓴 틱 {string.Join(" ", ticks)}) · 카드 {latest?.Tick} · 임시 파일 곁 {r1?.Sim.Tick} · 깨진 뒤 {r2?.Sim.Tick}");
            }
            // S7: 판이 다르면 '이어 볼 수 없다' 글 · 세계를 읽지 않는다 · 예외 없음 / 모르는 마디는 건너뛰고 없는 마디는 빈 채로
            {
                var meta = store.Write(show.SaveBundle(SaveKind.Quit));
                string card = Path.Combine(store.FolderOf(meta), "card.json");
                bool threw = false; string why = "", listed = ""; LiveShow? old = null; int reads0 = store.BinReads, reads1 = -1;
                try
                {
                    var node = JsonNode.Parse(File.ReadAllText(card))!;
                    node["WorldVersion"] = Sim.SaveVersion - 1;
                    File.WriteAllText(card, node.ToJsonString());
                    var m = store.List().FirstOrDefault(x => x.Folder == meta.Folder);
                    listed = m != null ? VoyageStore.Why(m) : "목록에 없다";
                    old = m != null ? LiveShow.Resume(store, m, out why) : null;
                    reads1 = store.BinReads;
                }
                catch (Exception e) { threw = true; why = e.Message; }
                // 모르는 마디 · 없는 마디
                var bundle = show.SaveBundle(SaveKind.Quit);
                bundle.Other["뒤 판의 마디"] = new byte[] { 1, 2, 3 };
                bundle.Screen = null;
                var back = VoyageBundle.FromBytes(bundle.ToBytes(), out string bwhy);
                LiveShow? fresh = null; bool threw2 = false;
                try { fresh = back != null ? LiveShow.Resume(back) : null; } catch (Exception e) { threw2 = true; bwhy = e.Message; }
                bool known = back != null && back.Other.TryGetValue("뒤 판의 마디", out var ob) && ob.Length == 3 && back.Screen == null && back.Record != null && back.Hands != null;
                bool text = why.Contains("앞선 판") && why.Contains("이어 볼 수 없다") && listed == why && TextLint.Bad(why) == null;
                Check("이어 보기 S7: 카드의 세계 판 ≠ 지금 판 → '이 항해는 앞선 판에서 남겼다 — 지금 판에서는 이어 볼 수 없다' · 세계를 읽지 않는다 · 예외 없음 / 모르는 마디는 건너뛰고 · 없는 마디는 빈 채로 이어 본다",
                    !threw && old == null && text && reads1 == reads0 && known && !threw2 && fresh != null && fresh.Sim.Tick == show.Sim.Tick,
                    $"글 '{why}' · 목록 글 같음 {listed == why} · 세계 읽기 {reads1 - reads0} · 예외 {threw}/{threw2} · 모르는 마디 남음 {known} · 이어 봄 {fresh?.Sim.Tick}/{show.Sim.Tick} {bwhy}");
            }
        }
        finally { try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch (IOException) { } }
    }
}
