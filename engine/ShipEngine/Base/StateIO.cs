using System.Runtime.InteropServices;
using System.Text;

namespace ShipEngine;

/// <summary>찍을 수 있는 상태. 정해진 순서로 쓰고, 같은 순서로 읽는다.</summary>
public interface IState
{
    void Save(StateWriter w);
    void Load(StateReader r);
}

/// <summary>찍기 쓰개 — 배열은 통째로 (빠르다).</summary>
public sealed class StateWriter
{
    private readonly MemoryStream _ms;
    private readonly BinaryWriter _w;

    public StateWriter(int capacity = 1 << 20)
    {
        _ms = new MemoryStream(capacity);
        _w = new BinaryWriter(_ms, Encoding.UTF8, leaveOpen: true);
    }

    public void Tag(string name) { _w.Write(Rng.Key(name)); } // 엇갈려 읽으면 바로 알 수 있게
    public void I32(int v) => _w.Write(v);
    public void I64(long v) => _w.Write(v);
    public void U64(ulong v) => _w.Write(v);
    public void F32(float v) => _w.Write(v);
    public void F64(double v) => _w.Write(v);
    public void Bool(bool v) => _w.Write(v);
    public void Byte(byte v) => _w.Write(v);
    public void Str(string? s) { _w.Write(s != null); if (s != null) _w.Write(s); }

    public void Arr<T>(T[] a) where T : unmanaged
    {
        _w.Write(a.Length);
        _w.Write(MemoryMarshal.AsBytes(a.AsSpan()));
    }

    public void List<T>(List<T> a) where T : unmanaged
    {
        _w.Write(a.Count);
        _w.Write(MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(a)));
    }

    public byte[] ToArray() { _w.Flush(); return _ms.ToArray(); }
}

/// <summary>찍기 읽개.</summary>
public sealed class StateReader
{
    private readonly BinaryReader _r;

    public StateReader(byte[] data) => _r = new BinaryReader(new MemoryStream(data, writable: false), Encoding.UTF8);

    public void Tag(string name)
    {
        ulong got = _r.ReadUInt64();
        if (got != Rng.Key(name)) throw new InvalidDataException($"찍기 순서가 어긋났다: '{name}' 자리");
    }
    public int I32() => _r.ReadInt32();
    public long I64() => _r.ReadInt64();
    public ulong U64() => _r.ReadUInt64();
    public float F32() => _r.ReadSingle();
    public double F64() => _r.ReadDouble();
    public bool Bool() => _r.ReadBoolean();
    public byte Byte() => _r.ReadByte();
    public string? Str() => _r.ReadBoolean() ? _r.ReadString() : null;

    public T[] Arr<T>() where T : unmanaged
    {
        int n = _r.ReadInt32();
        var a = new T[n];
        _r.BaseStream.ReadExactly(MemoryMarshal.AsBytes(a.AsSpan()));
        return a;
    }

    /// <summary>이미 있는 배열에 읽어 넣는다 (길이가 같아야 한다).</summary>
    public void ArrInto<T>(T[] a) where T : unmanaged
    {
        int n = _r.ReadInt32();
        if (n != a.Length) throw new InvalidDataException($"배열 길이가 다르다 ({n} ≠ {a.Length})");
        _r.BaseStream.ReadExactly(MemoryMarshal.AsBytes(a.AsSpan()));
    }

    public List<T> List<T>() where T : unmanaged
    {
        var arr = Arr<T>();
        return new List<T>(arr);
    }

    public bool AtEnd => _r.BaseStream.Position >= _r.BaseStream.Length;
}

/// <summary>지문 (FNV-1a 64).</summary>
public static class Fingerprint
{
    public static ulong Of(ReadOnlySpan<byte> data)
    {
        ulong h = 14695981039346656037UL;
        foreach (byte b in data) { h ^= b; h *= 1099511628211UL; }
        return h;
    }

    public static ulong Of<T>(T[] a) where T : unmanaged => Of(MemoryMarshal.AsBytes(a.AsSpan()));
}
