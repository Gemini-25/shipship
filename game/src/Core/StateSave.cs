using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace ShipSim.Core;

/// <summary>이 칸은 상태 저장에 넣지 않는다 — 다시 계산하면 되는 캐시 · 성능 측정 (불러온 뒤 기본값 · 빈 목록에서 다시 채운다).</summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class NotSavedAttribute : Attribute { }

/// <summary>
/// 상태 저장: 세계를 통째로 찍어 둔다. 시드 + 관찰자 기록을 처음부터 다시 돌리는 저장(SaveGame)은 결정론이 깨지면 같이 깨지고
/// 긴 항해일수록 불러오기가 오래 걸린다 — 찍어 둔 상태는 그 자리에서 바로 이어 간다.
///
/// 방식: World에서 닿는 모든 객체를 필드 단위로 적는다 (참조는 번호, 순환도 그대로).
/// · 카탈로그(활동 · 명세처럼 정적 읽기 전용 필드에 든 객체)는 "어느 필드의 몇 번째"로만 적고, 불러올 때 그 객체를 다시 가리킨다 (같은 객체인지로 견주는 곳이 있다).
/// · Dictionary · HashSet은 칸 배열 순서를 그대로 두고 해시 · 버킷만 다시 엮는다 (객체 해시는 프로세스마다 다르다 — 칸 순서가 같아야 같은 역사).
/// · 대리자(람다)는 메서드 이름 · 서명과 대상 객체로 적는다. 문자열과 대리자는 값으로 묶는다 (같은 객체인지는 내력마다 다르고 뜻이 없다).
/// · 형식마다 필드 목록을 함께 적어, 코드가 바뀌어 맞지 않으면 불러오지 않는다 (그때는 시드 + 기록으로 다시 돌린다).
/// 같은 상태를 찍으면 바이트까지 같다 — 되살려 이어 돌린 세계와 원래 세계를 찍어 견주면 빠진 상태가 바로 보인다 (--snaptest).
/// </summary>
public static class StateSave
{
    public const string Magic = "shipsim-snap 1";

    public sealed class MismatchException : Exception
    {
        public MismatchException(string msg) : base(msg) { }
    }

    /// <summary>저장 파일 머리: 무엇을 언제 찍었는지 (본문을 풀지 않고 보여 줄 수 있게).</summary>
    public sealed record Info(int Seed, string Ship, long Tick, uint Hash, string Day, int Crew);

    /// <summary>마지막으로 쓰고 읽은 통계 (객체 수 · 바이트 · 걸린 시간).</summary>
    public static (int objects, long rawBytes, long packedBytes, double ms, int dropped) LastWrite;
    public static (int objects, double ms) LastRead;

    // ─────────────────────────────── 쓰기 ───────────────────────────────

    public static byte[] Write(World w) => Write(w, out _);

    public static byte[] Write(World w, out byte[] raw)
    {
        var s = Begin(w);
        while (!s.Step(double.MaxValue)) { }
        raw = s.Raw!;
        return s.Pack();
    }

    /// <summary>처음 찍기 전에 한 번: 카탈로그 · 세계 형식 계획을 미리 만든다 (첫 자동 저장이 한 프레임에 몰리지 않게 — 게임을 띄울 때).</summary>
    public static void Warm()
    {
        Catalog();
        PlanOf(typeof(World));
    }

    /// <summary>나눠 찍기: 화면이 멈추지 않게 프레임마다 조금씩 (그동안 세계를 돌리지 않는다 — 찍는 사이 바뀌면 안 된다).</summary>
    public static Saver Begin(World w) => new(w);

    public sealed class Saver
    {
        private readonly MemoryStream _body = new();
        private readonly BinaryWriter _bw;
        private readonly GraphWriter _gw;
        private readonly byte[] _head;
        private readonly System.Diagnostics.Stopwatch _sw = new();
        public byte[]? Raw { get; private set; }
        public bool Done => Raw != null;
        public int Objects => _gw.ObjectCount;
        /// <summary>찍는 데 쓴 시간 (나눠 찍은 몫의 합).</summary>
        public double Ms => _sw.Elapsed.TotalMilliseconds;

        internal Saver(World w)
        {
            // 머리: 지문 · 날짜 · 수치 (지문 계산이 늦게 만드는 것이 있어 본문보다 먼저)
            var hm = new MemoryStream();
            var hw = new BinaryWriter(hm, Encoding.UTF8, leaveOpen: true);
            hw.Write(Magic);
            hw.Write(w.Seed);
            hw.Write(w.ShipKey);
            hw.Write(w.Tick);
            hw.Write(SaveGame.StateHash(w));
            hw.Write($"{w.Day}일차 {w.Clock}");
            hw.Write(w.Crew.Count(c => !c.Dead));
            var tunes = Tuning.NonDefault().ToList();
            hw.Write(tunes.Count);
            foreach (var (k, v) in tunes) { hw.Write(k); hw.Write(v); }
            hw.Flush();
            _head = hm.ToArray();
            _bw = new BinaryWriter(_body, Encoding.UTF8, leaveOpen: true);
            _gw = new GraphWriter(_bw);
            _gw.Start(w);
        }

        /// <summary>budgetMs만큼 찍는다. 다 찍었으면 true.</summary>
        public bool Step(double budgetMs)
        {
            if (Done) return true;
            _sw.Start();
            bool done = _gw.Step(budgetMs);
            _sw.Stop();
            if (!done) return false;
            _bw.Flush();
            Raw = _body.ToArray();
            return true;
        }

        /// <summary>머리 + 압축한 본문. 다 찍은 뒤라면 다른 스레드에서 불러도 된다 (세계를 보지 않는다).</summary>
        public byte[] Pack()
        {
            if (Raw == null) throw new InvalidOperationException("아직 다 찍지 않았다");
            var file = new MemoryStream();
            file.Write(_head);
            using (var gz = new GZipStream(file, CompressionLevel.Fastest, leaveOpen: true)) gz.Write(Raw, 0, Raw.Length);
            LastWrite = (_gw.ObjectCount, Raw.Length, file.Length, _sw.Elapsed.TotalMilliseconds, _gw.Dropped);
            return file.ToArray();
        }
    }

    /// <summary>점검용: 찍은 본문의 그 바이트가 어느 객체 · 필드인지 (바로 앞 표시와, 그 객체를 처음 가리킨 곳을 거슬러).</summary>
    public static string Describe(World w, long offset)
    {
        var body = new MemoryStream();
        var bw = new BinaryWriter(body, Encoding.UTF8, leaveOpen: true);
        SaveGame.StateHash(w);
        var writer = new GraphWriter(bw) { Trace = new() };
        writer.Start(w);
        writer.Step(double.MaxValue);
        int i = writer.Trace.FindLastIndex(x => x.at <= offset);
        if (i < 0) return "?";
        var sb = new StringBuilder(string.Join(" ‖ ", writer.Trace.Skip(Math.Max(0, i - 3)).Take(Math.Min(4, i + 1)).Select(x => $"{x.at}:{x.what}")));
        string what = writer.Trace[i].what;
        for (int depth = 0; depth < 8; depth++)
        {
            var m = System.Text.RegularExpressions.Regex.Match(what, "^#(\\d+)");
            if (!m.Success || !writer.Origin.TryGetValue(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), out var from)) break;
            sb.Append($" ⇐ {from}");
            what = from;
        }
        return sb.ToString();
    }

    private static string Pretty(Type t) => t.IsGenericType ? $"{t.Name.Split('`')[0]}<{string.Join(",", t.GetGenericArguments().Select(Pretty))}>" : t.IsArray ? Pretty(t.GetElementType()!) + "[]" : t.Name;

    public static Info Peek(byte[] data)
    {
        using var br = new BinaryReader(new MemoryStream(data), Encoding.UTF8);
        return ReadHeader(br, out _);
    }

    private static Info ReadHeader(BinaryReader br, out List<(string, float)> tunes)
    {
        string magic;
        try { magic = br.ReadString(); } catch (Exception e) when (e is EndOfStreamException or IOException or FormatException) { throw new FormatException("상태 저장 파일이 아니다"); }
        if (magic != Magic) throw new FormatException("상태 저장 파일이 아니다");
        int seed = br.ReadInt32();
        string ship = br.ReadString();
        long tick = br.ReadInt64();
        uint hash = br.ReadUInt32();
        string day = br.ReadString();
        int crew = br.ReadInt32();
        int nt = br.ReadInt32();
        tunes = new List<(string, float)>();
        for (int i = 0; i < nt; i++) tunes.Add((br.ReadString(), br.ReadSingle()));
        return new Info(seed, ship, tick, hash, day, crew);
    }

    // ─────────────────────────────── 읽기 ───────────────────────────────

    /// <summary>찍어 둔 세계를 되살린다. 코드가 바뀌어 필드가 맞지 않으면 MismatchException (그때는 시드 + 기록으로).</summary>
    public static World Read(byte[] data)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var ms = new MemoryStream(data);
        using var br = new BinaryReader(ms, Encoding.UTF8);
        var info = ReadHeader(br, out var tunes);
        var raw = new MemoryStream();
        try { using (var gz = new GZipStream(ms, CompressionMode.Decompress, leaveOpen: true)) gz.CopyTo(raw); }
        catch (InvalidDataException) { throw new FormatException("상태 저장 파일이 망가졌다"); }
        raw.Position = 0;
        // 그 항해를 찍을 때의 수치로 (사고 간격 · 이야기꾼 같은 전역 수치는 세계 밖에 있다)
        foreach (var (k, _) in tunes) if (Tuning.Find(k) == null) throw new MismatchException($"모르는 수치: {k}");
        var reader = new GraphReader(new BinaryReader(raw, Encoding.UTF8));
        World w;
        try { w = (World)reader.ReadRoot(); }
        catch (EndOfStreamException) { throw new FormatException("상태 저장 파일이 잘렸다"); }
        Tuning.ResetDefaults();
        foreach (var (k, v) in tunes) Tuning.Apply(k, v);
        LastRead = (reader.ObjectCount, sw.Elapsed.TotalMilliseconds);
        if (w.Tick != info.Tick) throw new FormatException("상태 저장 파일이 망가졌다 (틱이 맞지 않는다)");
        return w;
    }

    // ─────────────────────────────── 형식 계획 ───────────────────────────────

    private static readonly Assembly CoreAsm = typeof(World).Assembly;

    /// <summary>값을 적는 방법.</summary>
    private enum VK : byte { Bool, U8, I8, Char, I16, U16, I32, U32, I64, U64, F32, F64, IPtr, UPtr, Dec, Nullable, Struct, Ref }

    private sealed class FieldPlan
    {
        public required FieldInfo F;
        public required Plan P;
        public bool Bucket, Hash, Next;
    }

    /// <summary>형식마다 한 번 계산해 두는 것: 어떻게 적나 · 필드 · 화면 쪽인지 · 배열을 통째로 옮길 수 있나.</summary>
    private sealed class Plan
    {
        public required Type Type;
        public VK Kind;
        public Plan? Inner;              // Nullable의 속
        public FieldPlan[] Fields = Array.Empty<FieldPlan>();
        public FieldInfo[] Skipped = Array.Empty<FieldInfo>(); // NotSaved
        public bool Foreign, Coll, Unmanaged, NoPad;
        public Plan? Elem;               // 배열 원소
        public Action<BinaryWriter, Array>? BulkWrite;  // 원소를 바이트째로 (기본값 · 열거 · 빈틈 없는 값 구조체)
        public Action<BinaryReader, Array>? BulkRead;
        public int Size;                 // 값 형식 크기 (Unmanaged일 때)
    }

    private static readonly Dictionary<Type, Plan> Plans = new();
    private static readonly Plan RefPlan = new() { Type = typeof(object), Kind = VK.Ref };

    private static Plan PlanOf(Type t)
    {
        lock (Plans)
        {
            if (Plans.TryGetValue(t, out var p)) return p;
            p = Build(t);
            Plans[t] = p;
            return p;
        }
    }

    private static Plan Build(Type t)
    {
        var p = new Plan { Type = t };
        var bt = t.IsEnum ? Enum.GetUnderlyingType(t) : t;
        if (bt.IsPrimitive)
        {
            p.Kind = Type.GetTypeCode(bt) switch
            {
                TypeCode.Boolean => VK.Bool, TypeCode.Byte => VK.U8, TypeCode.SByte => VK.I8, TypeCode.Char => VK.Char,
                TypeCode.Int16 => VK.I16, TypeCode.UInt16 => VK.U16, TypeCode.Int32 => VK.I32, TypeCode.UInt32 => VK.U32,
                TypeCode.Int64 => VK.I64, TypeCode.UInt64 => VK.U64, TypeCode.Single => VK.F32, TypeCode.Double => VK.F64,
                _ => bt == typeof(IntPtr) ? VK.IPtr : bt == typeof(UIntPtr) ? VK.UPtr : throw new NotSupportedException($"저장할 수 없는 값: {bt}"),
            };
            p.Unmanaged = p.NoPad = true;
            p.Size = bt == typeof(bool) ? 1 : Marshal.SizeOf(bt == typeof(char) ? typeof(ushort) : bt);
            return p;
        }
        if (t == typeof(decimal)) { p.Kind = VK.Dec; return p; }
        if (Nullable.GetUnderlyingType(t) is Type nt) { p.Kind = VK.Nullable; p.Inner = PlanOf(nt); return p; }
        if (t.IsArray)
        {
            p.Kind = VK.Ref;
            var et = t.GetElementType()!;
            p.Elem = et.IsValueType ? PlanOf(et) : RefPlan;
            p.Foreign = Foreign(t);
            if (t.IsSZArray && et.IsValueType && p.Elem.Unmanaged && p.Elem.NoPad && !IsEntry(et))
            {
                var m = typeof(StateSave).GetMethod(nameof(BulkW), BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(et);
                p.BulkWrite = m.CreateDelegate<Action<BinaryWriter, Array>>();
                var r = typeof(StateSave).GetMethod(nameof(BulkR), BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(et);
                p.BulkRead = r.CreateDelegate<Action<BinaryReader, Array>>();
            }
            return p;
        }
        p.Kind = t.IsValueType ? VK.Struct : VK.Ref;
        p.Foreign = Foreign(t);
        p.Coll = IsDict(t) || IsSet(t);
        if (typeof(Delegate).IsAssignableFrom(t) || typeof(Type).IsAssignableFrom(t) || t == typeof(string)) return p;
        p.Fields = FieldsOf(t).Select(f => new FieldPlan
        {
            F = f,
            P = f.FieldType.IsValueType ? PlanOf(f.FieldType) : RefPlan,
            Bucket = (IsDict(t) || IsSet(t)) && f.Name == "_buckets",
            Hash = HashSlot(t, f),
            Next = NextSlot(t, f),
        }).ToArray();
        p.Skipped = SkippedOf(t);
        if (t.IsValueType && p.Skipped.Length == 0 && p.Fields.All(f => f.P.Unmanaged && f.P.Kind != VK.Nullable))
        {
            // 빈틈 없는 값 구조체만 바이트째로 (빈틈 바이트는 값이 정해지지 않아 같은 상태도 다르게 찍힌다)
            p.Unmanaged = true;
            p.Size = (int)typeof(Unsafe).GetMethod(nameof(Unsafe.SizeOf))!.MakeGenericMethod(t).Invoke(null, null)!;
            p.NoPad = p.Fields.All(f => f.P.NoPad) && p.Size == p.Fields.Sum(f => f.P.Size);
        }
        return p;
    }

    private static void BulkW<T>(BinaryWriter w, Array a) where T : unmanaged => w.Write(MemoryMarshal.AsBytes(((T[])a).AsSpan()));

    private static void BulkR<T>(BinaryReader r, Array a) where T : unmanaged
    {
        var span = MemoryMarshal.AsBytes(((T[])a).AsSpan());
        while (span.Length > 0)
        {
            int n = r.Read(span);
            if (n <= 0) throw new EndOfStreamException();
            span = span[n..];
        }
    }

    /// <summary>세계에 들어 있어도 되는 형식: 시뮬레이션(ShipSim.Core) · .NET 기본. 화면(Godot · ShipSim.View) 쪽은 떼어 낸다.</summary>
    private static bool Foreign(Type t)
    {
        while (t.IsArray) t = t.GetElementType()!;
        string ns = t.Namespace ?? "";
        return ns.StartsWith("Godot", StringComparison.Ordinal) || ns.StartsWith("ShipSim.View", StringComparison.Ordinal)
            || t.Assembly == CoreAsm && !ns.StartsWith("ShipSim.Core", StringComparison.Ordinal) && !ns.StartsWith("System", StringComparison.Ordinal) && ns.Length > 0;
    }

    /// <summary>저장할 인스턴스 필드 (바탕 형식부터, 같은 층에서는 이름 순) — 정적 · NotSaved는 뺀다.</summary>
    private static FieldInfo[] FieldsOf(Type t)
    {
        var chain = new List<Type>();
        for (var x = t; x != null && x != typeof(object) && x != typeof(ValueType); x = x.BaseType) chain.Add(x);
        chain.Reverse();
        var list = new List<FieldInfo>();
        foreach (var x in chain)
            list.AddRange(x.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(f => f.GetCustomAttribute<NotSavedAttribute>() == null)
                .OrderBy(f => f.Name, StringComparer.Ordinal));
        return list.ToArray();
    }

    private static FieldInfo[] SkippedOf(Type t)
    {
        var list = new List<FieldInfo>();
        for (var x = t; x != null && x != typeof(object); x = x.BaseType)
            list.AddRange(x.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(f => f.GetCustomAttribute<NotSavedAttribute>() != null));
        return list.ToArray();
    }

    /// <summary>형식 모양 (불러올 때 지금 코드와 견준다): 필드 이름 · 값 형식은 선언 순서(바이트째 옮기는 배치) · 열거는 바탕 형식.</summary>
    private static string Shape(Type t)
    {
        if (t.IsEnum) return "enum " + Enum.GetUnderlyingType(t).Name;
        var p = PlanOf(t);
        if (p.Fields.Length == 0 && p.Kind != VK.Struct) return "";
        var s = string.Join(",", p.Fields.Select(f => f.F.Name));
        if (t.IsValueType)
            s += "|" + string.Join(",", t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).OrderBy(f => f.MetadataToken).Select(f => f.Name));
        return s;
    }

    private static bool IsDict(Type t) => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Dictionary<,>);
    private static bool IsSet(Type t) => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(HashSet<>);

    private static bool IsEntry(Type t) => t.IsNested && t.IsGenericType && (t.DeclaringType == typeof(Dictionary<,>) || t.DeclaringType == typeof(HashSet<>));

    /// <summary>Dictionary · HashSet의 칸: 해시값은 적지 않는다 (불러온 뒤 다시 계산).</summary>
    private static bool HashSlot(Type structType, FieldInfo f) =>
        IsEntry(structType) && (structType.DeclaringType == typeof(Dictionary<,>) ? f.Name == "hashCode" : f.Name == "HashCode");

    /// <summary>칸의 사슬(next): 쓰는 칸이면 -1로 적는다 (버킷 사슬은 넣은 내력마다 달라 불러온 뒤 다시 엮는다 · 빈 칸 사슬은 그대로 — 다음에 넣을 자리).</summary>
    private static bool NextSlot(Type structType, FieldInfo f) =>
        IsEntry(structType) && (structType.DeclaringType == typeof(Dictionary<,>) ? f.Name == "next" : f.Name == "Next");

    // ─────────────────────────────── 정적 카탈로그 ───────────────────────────────

    /// <summary>정적 읽기 전용 필드에 든 객체 (그 안의 배열 · 목록 · 사전 값까지 두 겹) → 경로. 처음 한 번만 만든다.</summary>
    private static Dictionary<object, (FieldInfo field, int[] path)>? _catalog;
    private static readonly object CatalogLock = new();

    private static Dictionary<object, (FieldInfo field, int[] path)> Catalog()
    {
        lock (CatalogLock)
        {
            if (_catalog != null) return _catalog;
            var map = new Dictionary<object, (FieldInfo, int[])>(ReferenceEqualityComparer.Instance);
            var types = CoreAsm.GetTypes()
                .Where(t => (t.Namespace ?? "").StartsWith("ShipSim.Core", StringComparison.Ordinal) && !t.ContainsGenericParameters)
                .OrderBy(t => t.FullName, StringComparer.Ordinal);
            foreach (var t in types)
            {
                FieldInfo[] statics;
                try { statics = t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly); }
                catch (TypeLoadException) { continue; }
                foreach (var f in statics.Where(f => f.IsInitOnly && !f.IsLiteral && !f.FieldType.IsValueType && !NotCatalog(t, f)).OrderBy(f => f.Name, StringComparer.Ordinal))
                {
                    object? v;
                    try { v = f.GetValue(null); } catch (TypeInitializationException) { continue; }
                    Register(map, v, f, Array.Empty<int>(), 0);
                }
            }
            _catalog = map;
            return map;
        }
    }

    /// <summary>정적 읽기 전용이지만 카탈로그가 아닌 것: 돌면서 채우는 캐시 (세계의 객체를 담거나 프로세스마다 내용이 다르다).</summary>
    private static bool NotCatalog(Type t, FieldInfo f) =>
        t == typeof(StateSave) || t == typeof(Prof) || f.GetCustomAttribute<NotSavedAttribute>() != null
        || f.FieldType.IsGenericType && f.FieldType.GetGenericTypeDefinition() == typeof(ConditionalWeakTable<,>)
        || (t.Name, f.Name) is ("Crisis", "_holes") or ("ShipGenerator", "Cache") or ("MeteorShelter", "HullCount") or ("BrainSystem", "_cats");

    private static void Register(Dictionary<object, (FieldInfo, int[])> map, object? v, FieldInfo f, int[] path, int depth)
    {
        if (v == null || v is string || v.GetType().IsValueType || v is Type || v is Delegate) return;
        if (!map.ContainsKey(v)) map[v] = (f, path);
        if (depth >= 2) return;
        int i = 0;
        switch (v)
        {
            case Array a:
                if (a.Rank != 1 || a.GetType().GetElementType()!.IsValueType) return;
                foreach (var e in a) Register(map, e, f, Append(path, i++), depth + 1);
                break;
            case System.Collections.IDictionary d:
                foreach (var e in d.Values) Register(map, e, f, Append(path, i++), depth + 1);
                break;
            case System.Collections.IList l:
                foreach (var e in l) Register(map, e, f, Append(path, i++), depth + 1);
                break;
        }
    }

    private static int[] Append(int[] p, int i) { var r = new int[p.Length + 1]; p.CopyTo(r, 0); r[^1] = i; return r; }

    private static object? Resolve(FieldInfo f, int[] path)
    {
        object? v = f.GetValue(null);
        foreach (int i in path)
        {
            v = v switch
            {
                Array a => i < a.Length ? a.GetValue(i) : null,
                System.Collections.IDictionary d => d.Values.Cast<object?>().ElementAtOrDefault(i),
                System.Collections.IList l => i < l.Count ? l[i] : null,
                _ => null,
            };
        }
        return v;
    }

    /// <summary>.NET 쪽 싱글턴 (기본 비교자 등): 그 형식 · 바탕 형식 · 바깥 형식의 정적 필드에 바로 그 객체가 있으면 그 필드.
    /// 같은 형식의 싱글턴이 여럿일 수 있다 (문자열 비교자: 기본용 · 순서 비교용) — 형식마다 그런 필드를 모두 기억한다.</summary>
    private static readonly Dictionary<Type, FieldInfo[]> SystemSingletons = new();

    private static FieldInfo? FindSystemSingleton(object o, Type t)
    {
        FieldInfo[] fields;
        lock (SystemSingletons)
        {
            if (!SystemSingletons.TryGetValue(t, out fields!))
            {
                var found = new List<FieldInfo>();
                var seen = new HashSet<Type>();
                var queue = new Queue<Type>();
                for (var x = t; x != null && x != typeof(object); x = x.BaseType) { queue.Enqueue(x); if (x.DeclaringType != null) queue.Enqueue(x.DeclaringType); }
                while (queue.Count > 0)
                {
                    var x = queue.Dequeue();
                    if (!seen.Add(x) || x.ContainsGenericParameters) continue;
                    foreach (var f in x.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    {
                        if (f.IsLiteral || f.FieldType.IsValueType || !f.IsInitOnly) continue;
                        object? v;
                        try { v = f.GetValue(null); } catch { continue; }
                        if (v != null && v.GetType() == t) found.Add(f);
                    }
                }
                SystemSingletons[t] = fields = found.ToArray();
            }
        }
        foreach (var f in fields) if (ReferenceEquals(f.GetValue(null), o)) return f;
        return null;
    }

    /// <summary>.NET이 같이 쓰는 빈 배열: Array.Empty&lt;T&gt;() (1) · List&lt;T&gt;의 빈 배열 (2). 빈 목록들이 이걸 함께 가리킨다 — 되살려도 함께 가리키게.</summary>
    private static readonly Dictionary<Type, (object? empty, object? list)> Empties = new();

    private static (object? empty, object? list) EmptyArrays(Type et)
    {
        lock (Empties)
        {
            if (Empties.TryGetValue(et, out var e)) return e;
            object? a = null, l = null;
            try { a = typeof(Array).GetMethod(nameof(Array.Empty))!.MakeGenericMethod(et).Invoke(null, null); } catch (Exception) { }
            try { l = typeof(List<>).MakeGenericType(et).GetField("s_emptyArray", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null); } catch (Exception) { }
            return Empties[et] = (a, l);
        }
    }

    // ─────────────────────────────── 쓰는 쪽 ───────────────────────────────

    private enum Kind : byte { Null, Ref, New, StrNew, StrRef }
    private enum NewKind : byte { Object, Boxed, String, Array, Delegate, Type, Catalog, SystemStatic, EmptyArray }

    private sealed class GraphWriter
    {
        private readonly BinaryWriter _w;
        private readonly Dictionary<object, int> _ids = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<Type, int> _types = new();
        private readonly Dictionary<string, int> _strings = new(StringComparer.Ordinal);
        private readonly Dictionary<(Type, MethodInfo), Dictionary<object, int>> _delegates = new();
        private static readonly object NoTarget = new();
        private readonly Queue<object> _pending = new();
        private readonly Dictionary<object, (FieldInfo, int[])> _catalog = Catalog();
        private Array? _arr;     // 쓰는 중인 큰 배열 (나눠 찍을 때 그 자리부터 이어서)
        private Plan? _arrElem;
        private int _arrAt;
        private int _next;       // 객체 번호 (묶인 대리자는 같은 번호를 받으니 _ids.Count와 다르다)
        public int ObjectCount => _next;
        public int Dropped;
        public List<(long at, string what)>? Trace;
        public readonly Dictionary<int, string> Origin = new();
        private string _at = "", _last = "";

        public GraphWriter(BinaryWriter w) { _w = w; }

        private void Mark(string what) { if (Trace != null) { _last = $"{_at}{what}"; Trace.Add((_w.BaseStream.Position, _last)); } }
        private void Born(int id) { if (Trace != null) Origin[id] = _last; }

        public void Start(World w) => WriteRef(w);

        public bool Step(double budgetMs)
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            long limit = budgetMs >= double.MaxValue / 2 ? long.MaxValue : t0 + (long)(budgetMs * System.Diagnostics.Stopwatch.Frequency / 1000.0);
            int n = 0;
            while (true)
            {
                if (_arr != null)
                {
                    // 큰 배열은 원소 몇백 개마다 시간을 본다
                    while (_arrAt < _arr.Length)
                    {
                        if (Trace != null && (_arrAt < 64 || _arrAt % 64 == 0)) Mark($"[{_arrAt}]");
                        WriteVal(_arr.GetValue(_arrAt), _arrElem!);
                        _arrAt++;
                        if ((_arrAt & 255) == 0 && System.Diagnostics.Stopwatch.GetTimestamp() > limit) return false;
                    }
                    _arr = null;
                }
                if (_pending.Count == 0) return true;
                WriteBody(_pending.Dequeue());
                if ((++n & 31) == 0 && System.Diagnostics.Stopwatch.GetTimestamp() > limit) return false;
            }
        }

        private void WriteType(Type t)
        {
            if (_types.TryGetValue(t, out int id)) { _w.Write(id); return; }
            _types[t] = _types.Count;
            _w.Write(-1);
            if (t.IsArray)
            {
                _w.Write((byte)1);
                _w.Write(t.GetArrayRank());
                _w.Write(t.IsSZArray);
                WriteType(t.GetElementType()!);
            }
            else if (t.IsGenericType && !t.IsGenericTypeDefinition)
            {
                _w.Write((byte)2);
                WriteTypeName(t.GetGenericTypeDefinition());
                var args = t.GetGenericArguments();
                _w.Write(args.Length);
                foreach (var a in args) WriteType(a);
            }
            else
            {
                _w.Write((byte)0);
                WriteTypeName(t);
            }
            _w.Write(t.IsArray || t.IsPrimitive || t == typeof(string) || typeof(Delegate).IsAssignableFrom(t) || typeof(Type).IsAssignableFrom(t) ? "" : Shape(t));
        }

        private void WriteTypeName(Type t)
        {
            _w.Write(t.Assembly == CoreAsm ? "" : t.Assembly.GetName().Name ?? "");
            _w.Write(t.FullName ?? t.Name);
        }

        private void WriteRef(object? o)
        {
            if (o == null) { _w.Write((byte)Kind.Null); return; }
            if (o is string str)
            {
                if (_strings.TryGetValue(str, out int sid)) { _w.Write((byte)Kind.StrRef); _w.Write(sid); return; }
                _strings[str] = _strings.Count;
                _w.Write((byte)Kind.StrNew);
                _w.Write(str);
                return;
            }
            if (_ids.TryGetValue(o, out int id)) { _w.Write((byte)Kind.Ref); _w.Write(id); return; }
            var t = o.GetType();
            if (o is Delegate dg) { WriteDelegate(dg, t); return; }
            var plan = PlanOf(t);
            if (plan.Foreign) { Dropped++; _w.Write((byte)Kind.Null); return; }
            id = _next++; _ids[o] = id; Born(id);
            _w.Write((byte)Kind.New);
            if (_catalog.TryGetValue(o, out var cat))
            {
                _w.Write((byte)NewKind.Catalog);
                WriteType(cat.Item1.DeclaringType!);
                _w.Write(cat.Item1.Name);
                _w.Write(cat.Item2.Length);
                foreach (int i in cat.Item2) _w.Write(i);
                return;
            }
            if (o is Type ty) { _w.Write((byte)NewKind.Type); WriteType(ty); return; }
            if (t.IsPrimitive || t.IsEnum)
            {
                _w.Write((byte)NewKind.Boxed);
                WriteType(t);
                WriteVal(o, plan);
                return;
            }
            if (t.Assembly != CoreAsm && !t.IsArray && FindSystemSingleton(o, t) is FieldInfo sf)
            {
                _w.Write((byte)NewKind.SystemStatic);
                WriteType(sf.DeclaringType!);
                _w.Write(sf.Name);
                return;
            }
            if (t.IsArray)
            {
                var a = (Array)o;
                if (t.IsSZArray && a.Length == 0)
                {
                    var (ae, le) = EmptyArrays(t.GetElementType()!);
                    byte which = ReferenceEquals(o, ae) ? (byte)1 : ReferenceEquals(o, le) ? (byte)2 : (byte)0;
                    if (which > 0) { _w.Write((byte)NewKind.EmptyArray); WriteType(t.GetElementType()!); _w.Write(which); return; }
                }
                _w.Write((byte)NewKind.Array);
                WriteType(t);
                for (int r = 0; r < a.Rank; r++) _w.Write(a.GetLength(r));
                _pending.Enqueue(o);
                return;
            }
            _w.Write((byte)(t.IsValueType ? NewKind.Boxed : NewKind.Object));
            WriteType(t);
            _pending.Enqueue(o);
        }

        private void WriteDelegate(Delegate dg, Type t)
        {
            // 화면 쪽을 부르는 대리자는 떼어 낸다 (불러오면 화면이 다시 붙인다)
            var parts = dg.GetInvocationList().Where(d => d.Target == null ? !Foreign(d.Method.DeclaringType!) : !PlanOf(d.Target.GetType()).Foreign).ToArray();
            if (parts.Length == 0) { Dropped++; _w.Write((byte)Kind.Null); return; }
            // 같은 메서드 · 같은 대상이면 같은 대리자로 묶는다 (캡처 없는 람다는 .NET이 한 객체를 돌려쓴다 — 되살린 뒤엔 그 묶음이 달라진다)
            (Type, MethodInfo)? single = parts.Length == 1 ? (t, parts[0].Method) : null;
            object tkey = parts.Length == 1 ? parts[0].Target ?? NoTarget : NoTarget;
            if (single is { } sk && _delegates.TryGetValue(sk, out var byTarget) && byTarget.TryGetValue(tkey, out int did))
            {
                _ids[dg] = did;
                _w.Write((byte)Kind.Ref); _w.Write(did);
                return;
            }
            int id = _next++; _ids[dg] = id; Born(id);
            if (single is { } sk2)
            {
                if (!_delegates.TryGetValue(sk2, out var map)) _delegates[sk2] = map = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
                map[tkey] = id;
            }
            _w.Write((byte)Kind.New);
            _w.Write((byte)NewKind.Delegate);
            WriteType(t);
            _w.Write(parts.Length);
            foreach (var p in parts)
            {
                var m = p.Method;
                WriteType(m.DeclaringType!);
                _w.Write(m.Name);
                var ps = m.GetParameters();
                _w.Write(ps.Length);
                foreach (var pi in ps) WriteType(pi.ParameterType);
                if (m.IsGenericMethod) { var ga = m.GetGenericArguments(); _w.Write(ga.Length); foreach (var g in ga) WriteType(g); } else _w.Write(0);
                WriteRef(p.Target);
            }
        }

        private void WriteBody(object o)
        {
            var t = o.GetType();
            var p = PlanOf(t);
            if (Trace != null) _at = $"#{_ids[o]} {Pretty(t)}";
            if (o is Array a)
            {
                if (p.BulkWrite != null) { p.BulkWrite(_w, a); return; }
                if (a.Rank == 1) { _arr = a; _arrElem = p.Elem; _arrAt = 0; return; } // Step이 이어서 (나눠 찍기)
                foreach (var e in a) WriteVal(e, p.Elem!);
                return;
            }
            WriteFields(o, p);
        }

        private void WriteFields(object o, Plan p)
        {
            foreach (var f in p.Fields)
            {
                Mark($".{f.F.Name}");
                if (f.Bucket) { _w.Write((byte)Kind.Null); continue; }
                WriteVal(f.F.GetValue(o), f.P);
            }
        }

        private void WriteVal(object? v, Plan p)
        {
            switch (p.Kind)
            {
                case VK.Bool: _w.Write((bool)v!); return;
                case VK.U8: _w.Write((byte)v!); return;
                case VK.I8: _w.Write((sbyte)v!); return;
                case VK.Char: _w.Write((ushort)(char)v!); return;
                case VK.I16: _w.Write((short)v!); return;
                case VK.U16: _w.Write((ushort)v!); return;
                case VK.I32: _w.Write((int)v!); return;
                case VK.U32: _w.Write((uint)v!); return;
                case VK.I64: _w.Write((long)v!); return;
                case VK.U64: _w.Write((ulong)v!); return;
                case VK.F32: _w.Write((float)v!); return;
                case VK.F64: _w.Write((double)v!); return;
                case VK.IPtr: _w.Write(((IntPtr)v!).ToInt64()); return;
                case VK.UPtr: _w.Write(((UIntPtr)v!).ToUInt64()); return;
                case VK.Dec: _w.Write((decimal)v!); return;
                case VK.Nullable: _w.Write(v != null); if (v != null) WriteVal(v, p.Inner!); return; // 값이 없으면 상자도 null
                case VK.Struct:
                    foreach (var f in p.Fields)
                    {
                        Mark($"/{f.F.Name}");
                        if (f.Hash) { if (f.P.Kind == VK.U32) _w.Write(0u); else _w.Write(0); continue; }
                        var fv = f.F.GetValue(v);
                        if (f.Next && (int)fv! >= -1) fv = -1;
                        WriteVal(fv, f.P);
                    }
                    return;
                default: WriteRef(v); return; // 참조 (object · 인터페이스 칸이면 상자에 든 값도)
            }
        }
    }

    // ─────────────────────────────── 읽는 쪽 ───────────────────────────────

    private sealed class GraphReader
    {
        private readonly BinaryReader _r;
        private readonly List<object?> _objs = new();
        private readonly List<Type> _types = new();
        private readonly List<string> _strings = new();
        private readonly Queue<object> _pending = new();
        private readonly List<object> _rehash = new();
        public int ObjectCount => _objs.Count;

        public GraphReader(BinaryReader r) { _r = r; }

        public object ReadRoot()
        {
            var root = ReadRef() ?? throw new FormatException("상태 저장 파일이 비었다");
            while (_pending.Count > 0) ReadBody(_pending.Dequeue());
            foreach (var d in _rehash) Rehash(d);
            return root;
        }

        private Type ReadType()
        {
            int id = _r.ReadInt32();
            if (id >= 0) return _types[id];
            int slot = _types.Count;
            _types.Add(typeof(object)); // 자리 잡기 (안쪽 형식이 먼저 번호를 받는다)
            byte form = _r.ReadByte();
            Type t;
            if (form == 1)
            {
                int rank = _r.ReadInt32();
                bool sz = _r.ReadBoolean();
                var et = ReadType();
                t = sz ? et.MakeArrayType() : et.MakeArrayType(rank);
            }
            else if (form == 2)
            {
                var def = ReadTypeName();
                int n = _r.ReadInt32();
                var args = new Type[n];
                for (int i = 0; i < n; i++) args[i] = ReadType();
                t = def.MakeGenericType(args);
            }
            else t = ReadTypeName();
            _types[slot] = t;
            string shape = _r.ReadString();
            if (shape.Length > 0 && shape != Shape(t)) throw new MismatchException($"{t.Name}의 모양이 저장할 때와 다르다 (판이 바뀌었다)");
            return t;
        }

        private Type ReadTypeName()
        {
            string asm = _r.ReadString(), name = _r.ReadString();
            var t = asm.Length == 0 ? CoreAsm.GetType(name) : Type.GetType($"{name}, {asm}");
            return t ?? throw new MismatchException($"모르는 형식: {name}");
        }

        private object? ReadRef()
        {
            var k = (Kind)_r.ReadByte();
            switch (k)
            {
                case Kind.Null: return null;
                case Kind.Ref: return _objs[_r.ReadInt32()];
                case Kind.StrRef: return _strings[_r.ReadInt32()];
                case Kind.StrNew: { var str = _r.ReadString(); _strings.Add(str); return str; }
                case Kind.New: break;
                default: throw new FormatException("상태 저장 파일이 망가졌다");
            }
            var nk = (NewKind)_r.ReadByte();
            int slot = _objs.Count;
            _objs.Add(null);
            object o;
            switch (nk)
            {
                case NewKind.Delegate:
                {
                    var dt = ReadType();
                    int n = _r.ReadInt32();
                    Delegate? acc = null;
                    for (int i = 0; i < n; i++)
                    {
                        var decl = ReadType();
                        string name = _r.ReadString();
                        int np = _r.ReadInt32();
                        var ps = new Type[np];
                        for (int j = 0; j < np; j++) ps[j] = ReadType();
                        int ng = _r.ReadInt32();
                        var ga = new Type[ng];
                        for (int j = 0; j < ng; j++) ga[j] = ReadType();
                        var target = ReadRef();
                        var m = FindMethod(decl, name, ps, ga) ?? throw new MismatchException($"찾을 수 없는 동작: {decl.Name}.{name}");
                        var d = target == null ? Delegate.CreateDelegate(dt, m) : Delegate.CreateDelegate(dt, target, m);
                        acc = Delegate.Combine(acc, d);
                    }
                    o = acc!;
                    break;
                }
                case NewKind.Catalog:
                {
                    var decl = ReadType();
                    string fname = _r.ReadString();
                    int n = _r.ReadInt32();
                    var path = new int[n];
                    for (int i = 0; i < n; i++) path[i] = _r.ReadInt32();
                    var f = decl.GetField(fname, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                            ?? throw new MismatchException($"찾을 수 없는 목록: {decl.Name}.{fname}");
                    o = Resolve(f, path) ?? throw new MismatchException($"목록이 바뀌었다: {decl.Name}.{fname}");
                    break;
                }
                case NewKind.SystemStatic:
                {
                    var decl = ReadType();
                    string fname = _r.ReadString();
                    var f = decl.GetField(fname, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                            ?? throw new MismatchException($"찾을 수 없는 기본 객체: {decl.Name}.{fname}");
                    o = f.GetValue(null)!;
                    break;
                }
                case NewKind.EmptyArray:
                {
                    var et = ReadType();
                    byte which = _r.ReadByte();
                    var (ae, le) = EmptyArrays(et);
                    o = (which == 1 ? ae : le) ?? Array.CreateInstance(et, 0);
                    break;
                }
                case NewKind.Type: o = ReadType(); break;
                case NewKind.String: o = _r.ReadString(); break;
                case NewKind.Array:
                {
                    var at = ReadType();
                    int rank = at.GetArrayRank();
                    var lens = new int[rank];
                    for (int i = 0; i < rank; i++) lens[i] = _r.ReadInt32();
                    o = at.IsSZArray ? Array.CreateInstance(at.GetElementType()!, lens[0]) : Array.CreateInstance(at.GetElementType()!, lens);
                    _pending.Enqueue(o);
                    break;
                }
                case NewKind.Boxed:
                {
                    var bt = ReadType();
                    if (bt.IsPrimitive || bt.IsEnum) o = ReadVal(PlanOf(bt))!;
                    else { o = RuntimeHelpers.GetUninitializedObject(bt); _pending.Enqueue(o); }
                    break;
                }
                case NewKind.Object:
                {
                    var ot = ReadType();
                    o = RuntimeHelpers.GetUninitializedObject(ot);
                    Fresh(o, PlanOf(ot));
                    if (IsDict(ot) || IsSet(ot)) _rehash.Add(o);
                    _pending.Enqueue(o);
                    break;
                }
                default: throw new FormatException("상태 저장 파일이 망가졌다");
            }
            _objs[slot] = o;
            return o;
        }

        /// <summary>NotSaved 필드: 값 형식은 기본값, 빈 생성자가 있는 참조 형식(목록 · 사전)은 새로 — 다시 계산할 캐시.</summary>
        private static void Fresh(object o, Plan p)
        {
            foreach (var f in p.Skipped)
                if (!f.FieldType.IsValueType && !f.FieldType.IsAbstract && !f.FieldType.IsInterface && f.FieldType.GetConstructor(Type.EmptyTypes) != null)
                    f.SetValue(o, Activator.CreateInstance(f.FieldType));
        }

        private static MethodInfo? FindMethod(Type decl, string name, Type[] ps, Type[] ga)
        {
            foreach (var m in decl.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (m.Name != name) continue;
                var mm = m;
                if (ga.Length > 0) { if (!m.IsGenericMethodDefinition || m.GetGenericArguments().Length != ga.Length) continue; mm = m.MakeGenericMethod(ga); }
                if (mm.GetParameters().Select(p => p.ParameterType).SequenceEqual(ps)) return mm;
            }
            return null;
        }

        private void ReadBody(object o)
        {
            var t = o.GetType();
            var p = PlanOf(t);
            if (o is Array a)
            {
                if (p.BulkRead != null) { p.BulkRead(_r, a); return; }
                if (a.Rank == 1) { for (int i = 0; i < a.Length; i++) a.SetValue(ReadVal(p.Elem!), i); return; }
                // 여러 차원: 쓸 때와 같은 순서 (마지막 차원이 가장 빨리 돈다)
                var idx = new int[a.Rank];
                for (int n = 0; n < a.Length; n++)
                {
                    a.SetValue(ReadVal(p.Elem!), idx);
                    for (int r = a.Rank - 1; r >= 0; r--) { if (++idx[r] < a.GetLength(r)) break; idx[r] = 0; }
                }
                return;
            }
            foreach (var f in p.Fields)
            {
                if (f.Bucket) { ReadRef(); continue; }
                f.F.SetValue(o, ReadVal(f.P));
            }
        }

        private object? ReadVal(Plan p)
        {
            object v;
            switch (p.Kind)
            {
                case VK.Bool: v = _r.ReadBoolean(); break;
                case VK.U8: v = _r.ReadByte(); break;
                case VK.I8: v = _r.ReadSByte(); break;
                case VK.Char: v = (char)_r.ReadUInt16(); break;
                case VK.I16: v = _r.ReadInt16(); break;
                case VK.U16: v = _r.ReadUInt16(); break;
                case VK.I32: v = _r.ReadInt32(); break;
                case VK.U32: v = _r.ReadUInt32(); break;
                case VK.I64: v = _r.ReadInt64(); break;
                case VK.U64: v = _r.ReadUInt64(); break;
                case VK.F32: v = _r.ReadSingle(); break;
                case VK.F64: v = _r.ReadDouble(); break;
                case VK.IPtr: v = new IntPtr(_r.ReadInt64()); break;
                case VK.UPtr: v = new UIntPtr(_r.ReadUInt64()); break;
                case VK.Dec: return _r.ReadDecimal();
                case VK.Nullable: return _r.ReadBoolean() ? ReadVal(p.Inner!) : null;
                case VK.Struct:
                {
                    object box = RuntimeHelpers.GetUninitializedObject(p.Type);
                    foreach (var f in p.Fields) f.F.SetValue(box, ReadVal(f.P));
                    return box;
                }
                default: return ReadRef();
            }
            return p.Type.IsEnum ? Enum.ToObject(p.Type, v) : v;
        }

        /// <summary>칸 순서는 그대로 두고 해시 · 버킷만 다시 (빈 칸 사슬도 그대로 — 뒤에 넣는 자리가 저장 전과 같다).</summary>
        private static void Rehash(object d)
        {
            var t = d.GetType();
            bool dict = IsDict(t);
            var entriesF = t.GetField("_entries", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var bucketsF = t.GetField("_buckets", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var countF = t.GetField("_count", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var comparerF = t.GetField("_comparer", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var entries = (Array?)entriesF.GetValue(d);
            if (entries == null) { bucketsF.SetValue(d, null); return; }
            var et = entries.GetType().GetElementType()!;
            var hashF = et.GetField(dict ? "hashCode" : "HashCode", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
            var nextF = et.GetField(dict ? "next" : "Next", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
            var keyF = et.GetField(dict ? "key" : "Value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
            var keyType = t.GetGenericArguments()[0];
            var comparer = comparerF.GetValue(d);
            var hashM = comparer != null ? typeof(IEqualityComparer<>).MakeGenericType(keyType).GetMethod("GetHashCode")! : null;
            int count = (int)countF.GetValue(d)!;
            var buckets = new int[entries.Length];
            for (int i = 0; i < count; i++)
            {
                object e = entries.GetValue(i)!;
                int next = (int)nextF.GetValue(e)!;
                if (next < -1) continue; // 빈 칸 (사슬 그대로)
                object key = keyF.GetValue(e)!;
                int h = comparer != null ? (int)hashM!.Invoke(comparer, new[] { key })! : key.GetHashCode();
                uint uh = (uint)h;
                int b = (int)(uh % (uint)buckets.Length);
                hashF.SetValue(e, dict ? uh : (object)h);
                nextF.SetValue(e, buckets[b] - 1);
                buckets[b] = i + 1;
                entries.SetValue(e, i);
            }
            bucketsF.SetValue(d, buckets);
        }
    }
}
