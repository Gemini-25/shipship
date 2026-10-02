using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ShipSim.Core;

// v16.23 점검 항해 — 배 하나 · 시드 하나를 며칠 돌리며 1분마다 본다 (게임 상태는 읽기만).
public static partial class Program
{
    /// <summary>"살려고 하는 행동" — 대피 · 몸 피하기 · 우주복 · 구조 · 비상 배치 · 거들기 · 사고 대응 작업.</summary>
    private static readonly HashSet<string> AuditSurvIds = new()
    {
        "evacuate", "evasurvive", "evarescue", "suitmend", "takecover", "blastresponse", "roomcheck", "shelter", "heed", "muster",
        "cosmicevac", "cosmicshelter", "cosmicwarn", "cosmicbrace", "cosmicvigil", "quarantine", "recover", "refillsuit",
        "station", "help", "outage", "firebelief", "open-door",
    };

    private static bool AuditSurv(CrewMember c)
    {
        var j = c.Job;
        if (j == null) return false;
        if (j.Activity?.Id is string id && AuditSurvIds.Contains(id)) return true;
        return j.Order != null && WorkKinds.IsEmergency(j.Order.Kind);
    }

    private const int AuditWin = 30; // 죽기 전 몇 분을 보나

    private sealed class AuditCrewTrack
    {
        public readonly byte[] F = new byte[AuditWin];   // 1 스스로 대응 · 2 공황 · 4 쓰러짐 · 8 곁에서 대응
        public readonly int[] R = new int[AuditWin];
        public readonly byte[] Hp = new byte[AuditWin];
        public int N;
        public long PanicStart = -1;
        public bool Dead, Down;
        public string? Said;
        public int Diary;
    }

    private sealed class AuditProbe
    {
        private readonly World _w;
        private readonly AuditRun _run;
        private readonly long _t0;
        private readonly int _min = SimTime.Minutes(1);
        private readonly Dictionary<int, AuditCrewTrack> _crew = new();
        private readonly Dictionary<int, int> _roomSurv = new();
        private readonly Dictionary<int, int> _dwell = new();
        private readonly HashSet<int> _touchedRooms = new(), _touchedFix = new();
        private readonly HashSet<string> _acts = new(), _scenes = new(), _seenText = new();
        private readonly HashSet<string> _tech0;
        private readonly List<Room> _ess;
        private readonly AuditCompProbe _comp = new();
        // 고장
        private readonly Dictionary<Fault, (string key, int gen)> _faults = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<string, List<long>> _faultTicks = new();
        private readonly Dictionary<string, long> _cleared = new();
        private readonly Dictionary<string, (string what, string room)> _faultName = new();
        private int _gen;
        private readonly HashSet<int> _roomBreaker = new();
        private readonly Dictionary<Machine, int> _faultCount = new(ReferenceEqualityComparer.Instance);
        // 수확
        private readonly Dictionary<Machine, float> _crop = new(ReferenceEqualityComparer.Instance);
        // 보조 발전기
        private AAux? _aux;
        private long _auxStart, _auxLast;
        private string? _puller;
        // 로봇 · 드론
        private readonly Dictionary<int, bool> _robotBad = new(), _droneBad = new();
        private readonly Dictionary<int, DroneState> _droneState = new();
        private readonly Dictionary<int, RobotState> _robotState = new();
        // 오래 안 끝나는 일
        private readonly Dictionary<int, (int who, long since, float prog)> _claims = new();
        private readonly Dictionary<int, AStall> _stalls = new();
        // 기록
        private int _logVer;
        private long _alertSerial;
        private int _minutes;

        public AuditProbe(World w, AuditRun run)
        {
            _w = w;
            _run = run;
            _t0 = w.Tick;
            _logVer = w.Log.Version;
            _alertSerial = w.Alerts.Count > 0 ? w.Alerts[^1].Serial : 0;
            _tech0 = new HashSet<string>(w.Eras.Known);
            _ess = w.Ship.Rooms.Where(r => !r.Detached && (r.Type is RoomType.LifeSupport or RoomType.Bridge or RoomType.Medbay
                || r.Furniture.Any(f => f.Type == FurnitureType.MainComputer))).ToList();
            run.Crew = w.Crew.Count(c => !c.Dead);
            run.ShipName = w.Ship.Name;
            foreach (var f in w.Ship.Furniture)
                if (!f.Stowed && !f.Room.Detached) run.FixPresent[f.Type.ToString()] = run.FixPresent.GetValueOrDefault(f.Type.ToString()) + 1;
            run.RoomsTotal = w.Ship.Rooms.Count(r => !r.Detached && r.Type != RoomType.Corridor);
            run.Bots.Robots = w.Robots.Robots.Count;
            run.Bots.Drones = w.Drones.Drones.Count;
            Resources(true);
        }

        private float H(long tick) => (tick - _t0) / (float)SimTime.TicksPerHour;
        private string RoomName(int id) => id < 0 ? "-" : _w.Ship.Rooms.FirstOrDefault(r => r.Id == id)?.Name ?? "-";

        public void Minute()
        {
            var w = _w;
            long now = w.Tick;
            _minutes++;
            Texts();
            Crew(now);
            Faults(now);
            Aux(now);
            Bots();
            Claims(now);
            _comp.Minute(w, _run.Comp, _run.TextHits, _run.TextEx);
            if (_minutes % 10 == 0) Resources(false);
            if (_minutes % 60 == 0) foreach (var s in w.Scenes.Scenes) _scenes.Add(s.Kind.ToString());
        }

        private void Texts()
        {
            var w = _w;
            _puller = null;
            int add = w.Log.Version - _logVer;
            _logVer = w.Log.Version;
            var es = w.Log.Entries;
            for (int i = Math.Max(0, es.Count - add); i < es.Count; i++)
            {
                var e = es[i];
                AuditScanText(e.Text, "기록", _run.TextHits, _run.TextEx, _seenText);
                if (e.CrewId >= 0 && e.Text.Contains("보조 발전기 시동 손잡이")) _puller = w.Crew.FirstOrDefault(c => c.Id == e.CrewId)?.Name ?? "사람";
            }
            foreach (var a in w.Alerts)
            {
                if (a.Serial <= _alertSerial) continue;
                _alertSerial = a.Serial;
                AuditScanText(a.Text, "경보", _run.TextHits, _run.TextEx, _seenText);
            }
        }

        private void Crew(long now)
        {
            var w = _w;
            _roomSurv.Clear();
            foreach (var c in w.Crew)
                if (!c.Dead && !c.Down && AuditSurv(c)) _roomSurv[c.Room?.Id ?? -1] = _roomSurv.GetValueOrDefault(c.Room?.Id ?? -1) + 1;
            foreach (var c in w.Crew)
            {
                if (!_crew.TryGetValue(c.Id, out var tr)) _crew[c.Id] = tr = new AuditCrewTrack { Diary = c.Diary.Count };
                if (tr.Dead) continue;
                if (c.Dead) { tr.Dead = true; Death(c, tr, now); continue; }
                if (c.Down && !tr.Down) _run.Downs.Add(new ADown { Hour = H(now), Tick = now, Name = c.Name, CrewId = c.Id, RoomId = c.Room?.Id ?? -1, Room = c.Room?.Name ?? "선체 밖" });
                tr.Down = c.Down;
                bool self = !c.Down && AuditSurv(c);
                bool panic = c.Mind.Panicking(now);
                int room = c.Room?.Id ?? -1;
                bool near = c.CarriedBy != null || _roomSurv.GetValueOrDefault(room) - (self ? 1 : 0) > 0;
                int i = tr.N % AuditWin;
                tr.F[i] = (byte)((self ? 1 : 0) | (panic ? 2 : 0) | (c.Down ? 4 : 0) | (near ? 8 : 0));
                tr.R[i] = room;
                tr.Hp[i] = (byte)Math.Clamp((int)(c.Vitals.Health * 100f), 0, 100);
                tr.N++;
                if (panic && tr.PanicStart < 0) tr.PanicStart = now;
                else if (!panic && tr.PanicStart >= 0) { _run.PanicMin.Add((int)((now - tr.PanicStart) / _min)); tr.PanicStart = -1; }
                if (c.Room != null && c.Pose != Pose.Walking) _dwell[c.Room.Id] = _dwell.GetValueOrDefault(c.Room.Id) + 1;
                if (c.Job is Job j)
                {
                    _acts.Add(j.Activity?.Id ?? "(활동 없음) " + j.Label);
                    if (j.TargetRoom != null) _touchedRooms.Add(j.TargetRoom.Id);
                    if (j.Target != null) { _touchedFix.Add(j.Target.Id); _touchedRooms.Add(j.Target.Room.Id); }
                    if (j.Order?.Target.Furniture is Furniture of) { _touchedFix.Add(of.Id); _touchedRooms.Add(of.Room.Id); }
                }
                if (c.Said != null && c.Said != tr.Said) { tr.Said = c.Said; AuditScanText(c.Said, "말", _run.TextHits, _run.TextEx, _seenText); }
                if (c.Diary.Count < tr.Diary) tr.Diary = 0;
                for (int k = tr.Diary; k < c.Diary.Count; k++) AuditScanText(c.Diary[k].text, "일기", _run.TextHits, _run.TextEx, _seenText);
                tr.Diary = c.Diary.Count;
            }
            foreach (var rb in w.Robots.Robots)
                if (rb.Order?.Target.Furniture is Furniture rf) _touchedFix.Add(rf.Id);
        }

        private void Death(CrewMember c, AuditCrewTrack tr, long now)
        {
            int n = Math.Min(tr.N, AuditWin);
            long died = c.DiedAt >= 0 ? c.DiedAt : now;
            var d = new ADeath { Hour = H(died), Tick = died, Name = c.Name, CrewId = c.Id, Cause = c.Vitals.InjuryCause ?? "알 수 없음" };
            for (int k = 1; k <= n; k++)
            {
                int i = ((tr.N - k) % AuditWin + AuditWin) % AuditWin;
                byte f = tr.F[i];
                if ((f & 1) != 0) d.SelfMin++;
                if ((f & 2) != 0) { d.PanicMin++; if (k <= 10) d.Panic = true; }
                if ((f & 4) != 0) d.DownMin++;
                if ((f & 8) != 0) d.NearMin++;
                if (k == 1) { d.RoomId = tr.R[i]; d.Room = RoomName(tr.R[i]); }
                if (k == 3 && tr.Hp[i] >= 50) d.Sudden = true;
            }
            if (n < 3) d.Sudden = true;
            if (tr.PanicStart >= 0) { _run.PanicMin.Add((int)((now - tr.PanicStart) / _min)); tr.PanicStart = -1; }
            _run.Deaths.Add(d);
        }

        private void Faults(long now)
        {
            _gen++;
            foreach (var m in _w.Ship.Machines)
            {
                int seenNew = 0;
                foreach (var f in m.Faults)
                {
                    if (_faults.TryGetValue(f, out var known)) { _faults[f] = (known.key, _gen); continue; }
                    seenNew++;
                    string key = $"{m.Body.Id}:{f.Kind}:{f.Circuit}";
                    _faults[f] = (key, _gen);
                    _run.FaultEvents++;
                    if (!_faultName.ContainsKey(key))
                        _faultName[key] = ($"{m.Body.Name} · {f.Spec.Name}{(f.Circuit >= 0 ? $" · {PowerGrid.CircuitName(f.Circuit)} 회로" : "")}", m.Body.Room.Name);
                    if (!_faultTicks.TryGetValue(key, out var list)) _faultTicks[key] = list = new List<long>();
                    list.Add(now);
                    if (_cleared.TryGetValue(key, out long cl) && now - cl <= SimTime.Minutes(30))
                        _run.Refails.Add(new ARefail { What = _faultName[key].what, Room = _faultName[key].room, Hour = H(now), GapMin = (now - cl) / (float)_min });
                }
                // 1분 안에 났다가 고쳐진 고장 (차단기를 원격으로 바로 올리는 경우 등) — 고장 횟수가 는 만큼
                if (_faultCount.TryGetValue(m, out int fc0) && m.FaultCount - fc0 > seenNew)
                {
                    string key = $"{m.Body.Id}:quick";
                    _faultName.TryAdd(key, ($"{m.Body.Name} · 1분 안에 되돌린 고장", m.Body.Room.Name));
                    if (!_faultTicks.TryGetValue(key, out var ql)) _faultTicks[key] = ql = new List<long>();
                    for (int q = 0; q < m.FaultCount - fc0 - seenNew; q++)
                    {
                        _run.FaultEvents++;
                        ql.Add(now);
                        if (_cleared.TryGetValue(key, out long qc) && now - qc <= SimTime.Minutes(30))
                            _run.Refails.Add(new ARefail { What = _faultName[key].what, Room = _faultName[key].room, Hour = H(now), GapMin = (now - qc) / (float)_min });
                        _cleared[key] = now;
                    }
                }
                _faultCount[m] = m.FaultCount;
                if (m.Crop is CropState cs)
                {
                    if (_crop.TryGetValue(m, out float prev) && prev >= 0.5f && cs.Growth < prev - 0.4f)
                    {
                        string k = m.Body.Room.Kind.ToString();
                        _run.Harvest[k] = _run.Harvest.GetValueOrDefault(k) + 1;
                    }
                    _crop[m] = cs.Growth;
                }
            }
            // 방 차단기 (젖은 바닥 누전 등 — 설비 고장 목록 밖)
            foreach (var r in _w.Ship.Rooms)
            {
                bool off = r.BreakerOff;
                bool was = _roomBreaker.Contains(r.Id);
                if (off == was) continue;
                string key = $"room:{r.Id}:breaker";
                if (off)
                {
                    _roomBreaker.Add(r.Id);
                    _run.FaultEvents++;
                    _faultName.TryAdd(key, ($"{r.Name} 방 차단기 내려감", r.Name));
                    if (!_faultTicks.TryGetValue(key, out var list)) _faultTicks[key] = list = new List<long>();
                    list.Add(now);
                    if (_cleared.TryGetValue(key, out long cl) && now - cl <= SimTime.Minutes(30))
                        _run.Refails.Add(new ARefail { What = _faultName[key].what, Room = r.Name, Hour = H(now), GapMin = (now - cl) / (float)_min });
                }
                else { _roomBreaker.Remove(r.Id); _cleared[key] = now; }
            }
            List<Fault>? gone = null;
            foreach (var (f, v) in _faults)
                if (v.gen != _gen) (gone ??= new()).Add(f);
            if (gone != null)
                foreach (var f in gone) { _cleared[_faults[f].key] = now; _faults.Remove(f); }
        }

        private void Aux(long now)
        {
            var w = _w;
            var p = w.Power;
            bool essDark = _ess.Any(r => !r.Powered && !r.Detached && !r.BreakerOff && !r.PowerCut && r.PowerLinked); // v16.24 방 차단기 · 끊긴 선은 보조 발전기로 못 켠다 (그건 손 · 원격 차단기 일)
            bool low = p.BatteryPercent < 0.15f && p.Delivered < p.Demand * 0.95f;
            bool crisis = essDark || low;
            if (crisis)
            {
                if (_aux == null)
                {
                    bool has = w.Ship.FurnitureOf(FurnitureType.AuxGenerator).Any(f => f.Machine != null && !f.Machine.Has(FaultKind.Wrecked));
                    _aux = new AAux { Hour = H(now), Had = has && p.AuxFuel > 0f, Why = essDark ? "필수 방 정전" : "배터리 바닥" };
                    _auxStart = now;
                    if (p.AuxRunning) { _aux.AfterMin = 0f; _aux.By = "이미 돌고 있었다"; }
                }
                _auxLast = now;
            }
            if (_aux == null) return;
            if (_aux.AfterMin < 0f && p.AuxRunning) { _aux.AfterMin = (now - _auxStart) / (float)_min; _aux.By = _puller != null ? "사람 " + _puller : "컴퓨터"; }
            if (!crisis && now - _auxLast >= SimTime.Minutes(10)) CloseAux();
        }

        private void CloseAux()
        {
            if (_aux == null) return;
            _aux.Minutes = (_auxLast - _auxStart) / (float)_min + 1f;
            _run.Aux.Add(_aux);
            _aux = null;
        }

        private void Bots()
        {
            var b = _run.Bots;
            foreach (var r in _w.Robots.Robots)
            {
                b.RobotMin++;
                bool bad = r.Fault != null || r.State == RobotState.Stalled;
                if (r.State == RobotState.Active && !bad) b.RobotActive++;
                if (bad) b.RobotDown++;
                if (r.Fault != null && !_robotBad.GetValueOrDefault(r.Id)) b.RobotFaults++;
                _robotBad[r.Id] = r.Fault != null;
                if (r.State == RobotState.Lost && _robotState.GetValueOrDefault(r.Id) != RobotState.Lost) b.RobotLost++;
                _robotState[r.Id] = r.State;
            }
            foreach (var d in _w.Drones.Drones)
            {
                b.DroneMin++;
                bool bad = d.Faulty || d.Wrecked;
                if (d.State is DroneState.Outbound or DroneState.Working or DroneState.Returning or DroneState.Towing && !bad) b.DroneActive++;
                if (bad) b.DroneDown++;
                if (bad && !_droneBad.GetValueOrDefault(d.Id)) b.DroneFaults++;
                _droneBad[d.Id] = bad;
                var prev = _droneState.GetValueOrDefault(d.Id);
                if (d.State == DroneState.Adrift && prev != DroneState.Adrift) b.DroneAdrift++;
                if (d.State == DroneState.Lost && prev != DroneState.Lost) b.DroneLost++;
                _droneState[d.Id] = d.State;
            }
        }

        private void Claims(long now)
        {
            foreach (var o in _w.Board.All)
            {
                if (o.Closed || o.Assignee == null) { _claims.Remove(o.Id); continue; }
                if (!_claims.TryGetValue(o.Id, out var c) || c.who != o.Assignee.Id || MathF.Abs(c.prog - o.Progress) > 1e-4f
                    || o.Assignee.Job?.Order == o && o.Assignee.Pose == Pose.Working) // v16.24 손을 대고 있으면 (긴급 우회 · 부분 복구 · 실패 뒤 다시) 멈춘 게 아니다
                {
                    _claims[o.Id] = (o.Assignee.Id, now, o.Progress);
                    continue;
                }
                float hours = (now - c.since) / (float)SimTime.TicksPerHour;
                if (hours < 3f) continue;
                if (!_stalls.TryGetValue(o.Id, out var s))
                    _stalls[o.Id] = s = new AStall { Hour = H(c.since), What = o.Title, Who = o.Assignee.Name, Room = o.Target.Room?.Name ?? "-" };
                s.Hours = hours;
            }
        }

        private static readonly (string key, float empty)[] ResKeys =
            { ("식량", 0.5f), ("물", 1f), ("공기 탱크", 1f), ("배터리", 1f), ("금속판", 0.5f), ("실링폼", 0.5f) };

        private float Level(string key) => key switch
        {
            "식량" => ResourceLedger.Level(_w, "food"),
            "물" => _w.Water.Level,
            "공기 탱크" => ResourceLedger.Level(_w, "air"),
            "배터리" => _w.Power.BatteryPercent * 100f,
            "금속판" => _w.Board.Have(ItemKind.Plate),
            _ => _w.Board.Have(ItemKind.Sealant),
        };

        private void Resources(bool first)
        {
            foreach (var (key, empty) in ResKeys)
            {
                float v = Level(key);
                if (first) { _run.ResStart[key] = v; _run.ResMin[key] = v; continue; }
                if (v < _run.ResMin[key]) _run.ResMin[key] = v;
                if (v <= empty && _run.ResStart[key] > empty && !_run.ResOut.ContainsKey(key)) _run.ResOut[key] = H(_w.Tick);
            }
        }

        public void End()
        {
            var w = _w;
            long now = w.Tick;
            CloseAux();
            foreach (var tr in _crew.Values)
                if (tr.PanicStart >= 0 && !tr.Dead) _run.PanicMin.Add((int)((now - tr.PanicStart) / _min));
            _comp.End(w, _run.Comp, _run.TextHits, _run.TextEx);
            foreach (var s in w.Scenes.Scenes) _scenes.Add(s.Kind.ToString());
            // 같은 고장이 6시간 안에 세 번 넘게
            foreach (var (key, ticks) in _faultTicks.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                int best = 0, bi = 0;
                for (int i = 0, j = 0; i < ticks.Count; i++)
                {
                    while (ticks[i] - ticks[j] > SimTime.Hours(6)) j++;
                    if (i - j + 1 > best) { best = i - j + 1; bi = j; }
                }
                if (best >= 3)
                    _run.Repeats.Add(new ARepeat { What = _faultName[key].what, Room = _faultName[key].room, Count = best, First = H(ticks[bi]), Last = H(ticks[bi + best - 1]) });
            }
            // 사고 규모 · 연쇄 · 사고당 사망
            var cases = w.Scale.Cases.Where(k => k.Start >= _t0).ToList();
            var map = new Dictionary<ScaleCase, ACase>();
            foreach (var k in cases)
            {
                int chain = 1;
                if (k.Root >= 0 && w.Causes.IncidentOf(k.Root) is CauseIncident inc)
                    chain = inc.Nodes.Count == 0 ? 1 : inc.Nodes.Max(id => w.Causes.Node(id).Depth) + 1;
                var a = new ACase
                {
                    Key = k.Key, Name = k.Name, Room = RoomName(k.RoomId), Base = (int)k.Base, Peak = (int)k.Peak, Chain = chain, Spread = k.Rooms.Count,
                    Hour = H(k.Start), Hours = ((k.End >= 0 ? k.End : now) - k.Start) / (float)SimTime.TicksPerHour,
                };
                map[k] = a;
                _run.Cases.Add(a);
                AuditScanText(k.Name, "사고 이름", _run.TextHits, _run.TextEx, _seenText);
                AuditScanText(k.Plan, "사고 대응", _run.TextHits, _run.TextEx, _seenText);
                AuditScanText(k.Broadcast, "방송", _run.TextHits, _run.TextEx, _seenText);
                AuditScanText(k.Suggest, "제안", _run.TextHits, _run.TextEx, _seenText);
            }
            ScaleCase? Attribute(long tick, int crewId, int roomId)
            {
                ScaleCase? best = null;
                foreach (var k in cases)
                {
                    if (k.Start > tick || (k.End >= 0 && tick > k.End + SimTime.Hours(1))) continue;
                    bool hit = k.CrewId == crewId || (roomId >= 0 && (k.RoomId == roomId || k.Rooms.Contains(roomId))) || k.Peak >= IncidentScale.Ship;
                    if (!hit) continue;
                    if (best == null || k.Peak > best.Peak || (k.Peak == best.Peak && k.Start > best.Start)) best = k;
                }
                return best;
            }
            foreach (var d in _run.Deaths)
                if (Attribute(d.Tick, d.CrewId, d.RoomId) is ScaleCase k) { d.Scale = (int)k.Peak; d.Case = k.Name; map[k].Deaths++; }
            foreach (var d in _run.Downs)
                if (Attribute(d.Tick, d.CrewId, d.RoomId) is ScaleCase k) { d.Scale = (int)k.Peak; map[k].Downs++; }
            _run.Keys = cases.SelectMany(k => k.KeysSeen.Append(k.Key)).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
            // 쓰임
            foreach (var r in w.Ship.Rooms.Where(r => r.Type != RoomType.Corridor && !r.Merged).OrderBy(r => r.Id))
                _run.Rooms.Add(new ARoom { Kind = r.Kind.ToString(), Name = r.Name, Dwell = _dwell.GetValueOrDefault(r.Id), Touched = _touchedRooms.Contains(r.Id) ? 1 : 0 });
            foreach (var f in w.Ship.Furniture.Where(f => _touchedFix.Contains(f.Id)))
                _run.FixTouched[f.Type.ToString()] = _run.FixTouched.GetValueOrDefault(f.Type.ToString()) + 1;
            _run.Acts = _acts.OrderBy(x => x, StringComparer.Ordinal).ToList();
            _run.Daily = w.Daily.Stats.Seen.OrderBy(x => x, StringComparer.Ordinal).ToList();
            _run.Scenes = _scenes.OrderBy(x => x, StringComparer.Ordinal).ToList();
            _run.TechGained = w.Eras.Known.Where(t => !_tech0.Contains(t)).OrderBy(x => x, StringComparer.Ordinal).ToList();
            foreach (var day in w.Ledger.Days)
                if (day.Flows.TryGetValue("food", out var fl)) { _run.FoodIn += fl.In; _run.FoodOut += fl.Out; }
            for (int i = 0; i < FoodSourceSystem.Count; i++) if (w.FoodSources.In[i] > 0) _run.FoodWays[FoodSourceSystem.Name((FoodSrc)i)] = w.FoodSources.In[i]; // v16.22
            _run.Alive = w.Crew.Count(c => !c.Dead);
            var cs = w.Casualty; _run.Bleeds = cs.Bleeds; _run.Arrests = cs.Arrests; _run.Revived = cs.Revived; _run.TraumaDied = cs.Died; _run.Flashes = cs.Flashes; _run.WorkHurts = cs.WorkHurts; _run.WorkBad = cs.WorkBad; _run.Paged = cs.Paged; _run.DarkFalls = w.Body.Stats.DarkFalls; // v16.24
            var pr = w.Perils; _run.HeatStrokes = pr.HeatStrokes; _run.HeatDeaths = pr.HeatDeaths; _run.RadSevere = pr.RadSevere; _run.RadCollapses = pr.RadCollapses; _run.RadDeaths = pr.RadDeaths; _run.LateWakes = pr.LateWakes; _run.MaxDose10 = (int)(10f * w.Crew.Select(c => c.Dose).DefaultIfEmpty(0f).Max()); // v16.26
            _run.RoomsLost = w.Ship.Rooms.Count(r => r.Detached && !r.Merged && r.Type != RoomType.Corridor);
            _run.Stalls = _stalls.Values.OrderBy(s => s.Hour).ToList();
        }
    }

    /// <summary>항해 하나를 돌린다 (이 프로세스 안에서).</summary>
    private static AuditRun AuditOne(string ship, int seed, float days)
    {
        var run = new AuditRun { Ship = ship, Seed = seed, Days = days };
        var sw = Stopwatch.StartNew();
        World? w = null;
        AuditProbe? probe = null;
        try
        {
            w = World.CreateDefault(seed, 0, ship);
            w.CrewCanDie = true;
            w.Log.Capacity = 4000;
            probe = new AuditProbe(w, run);
            Prof.Reset();
            Prof.On = true;
            long total = (long)(days * SimTime.TicksPerDay);
            int step = SimTime.Minutes(1);
            for (long t = 1; t <= total; t++)
            {
                w.Step();
                if (t % step == 0) probe.Minute();
            }
        }
        catch (Exception e)
        {
            string at = e.StackTrace?.Split('\n').FirstOrDefault(l => l.Contains("ShipSim"))?.Trim() ?? "";
            run.Error = $"{e.GetType().Name}: {e.Message} {at}";
            run.ErrorHour = w != null ? (w.Tick - SimTime.Hours(7)) / (float)SimTime.TicksPerHour : 0f;
        }
        Prof.On = false;
        try { probe?.End(); } catch (Exception e) { run.Error ??= $"점검 마무리 중 {e.GetType().Name}: {e.Message}"; }
        run.Wall = sw.Elapsed.TotalSeconds;
        run.SecPerDay = run.Wall / Math.Max(0.01, days);
        if (w != null) run.Hash = SaveGame.StateHash(w);
        run.Prof = Prof.Report().Where(x => x.key.StartsWith("sys.") || x.key.StartsWith("crew."))
            .Take(5).Select(x => new AProf { Key = x.key, Ms = Math.Round(x.ms / Math.Max(0.01, days)) }).ToList();
        return run;
    }
}
