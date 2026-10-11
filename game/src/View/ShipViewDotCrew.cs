using System;
using System.Collections.Generic;
using Godot;
using ShipSim.Art;
using ShipSim.Core;

namespace ShipSim.View;

// 도트 사람: 시뮬레이션의 사람(나이 · 몸 · 머리 · 직업 · 상처 · 자세 · 든 것)을 도트 겉모습으로 옮기고,
// 쓰는 장면만 그때그때 그려 텍스처로 둔다 (한 프레임에 새로 그리는 수를 정해 두어 튀지 않게).
// 가까이(확대 1 이상)는 도트가 또렷하게(가장 가까운 칸), 그보다 멀면 부드럽게 줄여 그린다.
// 읽기만 한다 — 결정론과 상관없다.
public partial class ShipView
{
    /// <summary>도트 사람을 쓰나 (끄면 예전 인형).</summary>
    public static bool DotCrew { get; set; } = true;

    private sealed class DotSprite
    {
        public ImageTexture Tex = null!;
        public CanvasTexture Crisp = null!;
        public long Used;
    }

    private readonly Dictionary<(ulong sig, byte face, byte pose, byte frame, byte variant), DotSprite> _dotCache = new();
    private readonly Dictionary<int, PersonLook> _dotLooks = new();
    private int _dotBudget;
    private long _dotFrame;
    /// <summary>시험 · 성능 측정: 지금까지 새로 그린 장면 수 · 캐시 크기.</summary>
    public int DotBuilt { get; private set; }
    public int DotCached => _dotCache.Count;

    private void DotFrameStart()
    {
        _dotFrame = (long)Engine.GetProcessFrames();
        _dotBudget = 24;
        if ((_dotFrame & 511) == 0 && _dotCache.Count > 600)
        {
            // 오래 안 쓴 장면은 버린다 (옷 · 상처 · 든 것이 바뀌어 다시는 안 쓰는 것)
            var old = new List<(ulong, byte, byte, byte, byte)>();
            foreach (var (k, v) in _dotCache) if (_dotFrame - v.Used > 900) old.Add(k);
            foreach (var k in old) _dotCache.Remove(k);
        }
    }

    /// <summary>사람 하나의 도트 겉모습 (같은 사람은 같은 객체를 고쳐 쓴다).</summary>
    private PersonLook DotLookOf(CrewMember c, PuppetSpec spec)
    {
        if (!_dotLooks.TryGetValue(c.Id, out var l)) _dotLooks[c.Id] = l = new PersonLook();
        var b = _world.Body2.Peek(c);
        l.Seed = c.Id;
        l.Female = NameGen.LooksFeminine(c.Name) ?? (DotHash.U(c.Id, 77) & 1) == 0;
        float age = c.Age;
        l.Age = c.IsChild || age < 13f ? DotAge.Child : age < 19f ? DotAge.Teen : age < 45f ? DotAge.Adult : age < 60f ? DotAge.Middle : DotAge.Elder;
        float hcm = b?.HeightCm ?? 170f;
        l.Height = Math.Clamp((int)MathF.Round((hcm - (l.Female ? 163f : 175f)) / 5f), -2, 2);
        float kg = b?.Kg ?? 70f, bmi = kg / ((hcm / 100f) * (hcm / 100f));
        l.Build = bmi < 19.5f ? DotBuild.Thin : bmi > 28f ? DotBuild.Heavy : c.Fitness > 0.8f && bmi > 23f ? DotBuild.Strong : DotBuild.Normal;
        l.Skin = (b?.Skin ?? (byte)(c.Id % 6)) % 6;
        l.HairRamp = Ramps.Hair[(b?.HairColor ?? 1) % Ramps.Hair.Length];
        var style = b?.Style ?? HairStyle.Crop;
        l.Hair = style switch
        {
            HairStyle.Buzz => DotHair.Buzz, HairStyle.Side => DotHair.Side, HairStyle.Curly => DotHair.Curly, HairStyle.Bob => DotHair.Bob,
            HairStyle.Long => DotHair.Long, HairStyle.Ponytail => DotHair.Ponytail, HairStyle.Bun => DotHair.Bun, HairStyle.Braid => DotHair.Braid,
            HairStyle.Bald => DotHair.Bald, _ => DotHair.Crop,
        };
        if (b != null && b.HairCm < 1.2f && l.Hair != DotHair.Bald) l.Hair = DotHair.Buzz;
        if (l.Age == DotAge.Elder && !l.Female && l.Hair is DotHair.Crop or DotHair.Side or DotHair.Buzz && (DotHash.U(c.Id, 5) & 1) == 0) l.Hair = DotHair.Receding;
        l.Beard = b is { Stubbly: true } && b.BeardMm >= 6f;
        l.Stubble = b is { Stubbly: true } && b.BeardMm is >= 1.5f and < 6f;
        l.Mustache = false;
        float glassChance = l.Age switch { DotAge.Elder => 0.45f, DotAge.Middle => 0.25f, DotAge.Adult => 0.12f, DotAge.Teen => 0.08f, _ => 0.03f };
        l.Glasses = DotHash.F(c.Id, 11) < glassChance;
        (l.Cloth, l.Badge) = c.Role switch
        {
            CrewRole.Engineer => (Ramps.ClothOrange, 1),
            CrewRole.Medic => (Ramps.ClothWhite, 2),
            CrewRole.Pilot => (Ramps.ClothBlue, 3),
            CrewRole.Technician => (Ramps.ClothTeal, 4),
            CrewRole.Botanist => (Ramps.ClothGreen, 5),
            CrewRole.Electrician => (Ramps.ClothPurple, 6),
            _ => (Ramps.ClothBeige, 0),
        };
        var pc = Palette.Crew(c.Id);
        l.Accent = Ramps.Derive((byte)(pc.R * 255), (byte)(pc.G * 255), (byte)(pc.B * 255));
        l.Pattern = (int)(b?.Pattern ?? OutfitPattern.Plain);
        l.Casual = c.IsChild || c.Passenger;
        l.Suit = c.Suit != null;
        l.Dead = c.Dead;
        l.EyesClosed = c.Dead || c.Down || c.Pose == Pose.Sleeping;
        l.Cane = l.Age == DotAge.Elder && (c.Vitals.Frailty > 0.15f || age > 72f);
        l.Held = spec.Held switch
        {
            HeldThing.Tool or HeldThing.Scissors => DotHeld.Wrench,
            HeldThing.Cup => DotHeld.Cup,
            HeldThing.Box or HeldThing.Crate or HeldThing.Packet or HeldThing.Suit => DotHeld.Box,
            HeldThing.Extinguisher => DotHeld.Extinguisher,
            HeldThing.Mop => DotHeld.Mop,
            HeldThing.MedKit => DotHeld.MedKit,
            HeldThing.Plate => DotHeld.Plate,
            HeldThing.Cable => DotHeld.Cable,
            _ => DotHeld.None,
        };
        // 몸의 상처 → 붕대 · 깁스 · 팔걸이 · 잃은 팔다리 · 의수 · 화상 · 피
        var hurt = DotHurt.None;
        foreach (var x in c.Vitals.Wounds)
        {
            float sev = Wounds.Severity(c.Vitals, x);
            bool arm = Wounds.IsArm(x.Part), leg = Wounds.IsLeg(x.Part);
            bool left = x.Part is BodyPart.LeftArm or BodyPart.LeftLeg;
            if (x.Lost)
            {
                if (arm) hurt |= x.Prosthetic ? (left ? DotHurt.MetalArmL : DotHurt.MetalArmR) : (left ? DotHurt.NoArmL : DotHurt.NoArmR);
                else if (leg) hurt |= x.Prosthetic ? (left ? DotHurt.MetalLegL : DotHurt.MetalLegR) : (left ? DotHurt.NoLegL : DotHurt.NoLegR);
                continue;
            }
            if (sev < 0.15f) continue;
            switch (x.Kind)
            {
                case WoundKind.Burn: hurt |= DotHurt.Burn; break;
                case WoundKind.Radiation: hurt |= DotHurt.RadHair; break;
                case WoundKind.Toxic: hurt |= DotHurt.Sick; break;
                case WoundKind.Barotrauma: hurt |= DotHurt.Bruise; break;
            }
            if (x.Kind is WoundKind.Cut or WoundKind.Crush or WoundKind.Fracture)
            {
                if (x.Part == BodyPart.Head) hurt |= DotHurt.HeadWrap;
                else if (x.Part is BodyPart.Chest or BodyPart.Lungs) hurt |= DotHurt.TorsoWrap;
                else if (arm) hurt |= x.Kind == WoundKind.Fracture ? (left ? DotHurt.SlingL : DotHurt.SlingR) : (left ? DotHurt.CastArmL : DotHurt.CastArmR); // 부러지면 팔걸이 · 찢기면 붕대
                else if (leg) hurt |= left ? DotHurt.CastLegL : DotHurt.CastLegR;
                if (x.Kind == WoundKind.Cut && sev > 0.4f) hurt |= DotHurt.Bleed;
            }
        }
        if (c.Dose > 2f) hurt |= DotHurt.RadHair;
        if (c.InfectedAt >= 0 && !c.Immune || c.Fx.Worst > 0.4f) hurt |= DotHurt.Sick | DotHurt.Sweat;
        float temp = c.Room?.Air.Temperature ?? 21f;
        if (temp > 32f) hurt |= DotHurt.Sweat;
        if (temp < 6f && !l.Suit) hurt |= DotHurt.Frost;
        l.Hurt = hurt;
        return l;
    }

    private static ulong DotSig(PersonLook l)
    {
        unchecked
        {
            ulong h = 1469598103934665603UL;
            void M(long v) { h ^= (ulong)v; h *= 1099511628211UL; }
            M(l.Female ? 1 : 0); M((int)l.Age); M((int)l.Build); M(l.Height); M(l.Skin); M(l.HairRamp); M((int)l.Hair); M(l.Cloth); M(l.Accent);
            M(l.Pattern); M(l.Badge); M(l.Beard ? 1 : 0); M(l.Mustache ? 1 : 0); M(l.Stubble ? 1 : 0); M(l.Glasses ? 1 : 0); M(l.Cane ? 1 : 0);
            M(l.Casual ? 1 : 0); M(l.Suit ? 1 : 0); M(l.EyesClosed ? 1 : 0); M(l.Dead ? 1 : 0); M((long)l.Hurt); M((int)l.Held); M(l.Seed);
            return h;
        }
    }

    /// <summary>장면 하나의 텍스처 (없으면 예산 안에서 그린다 · 예산이 다 되면 null).</summary>
    private DotSprite? DotGet(PersonLook l, ulong sig, DotFacing f, DotPose p, int frame, byte variant)
    {
        var key = (sig, (byte)f, (byte)p, (byte)frame, variant);
        if (_dotCache.TryGetValue(key, out var s)) { s.Used = _dotFrame; return s; }
        if (_dotBudget <= 0) return null;
        _dotBudget--;
        DotBuilt++;
        Dot d;
        switch (variant)
        {
            case 1: d = DotPeople.Draw(l, DotFacing.S, DotPose.Stand, 0).Rotate(cw: false); break;      // 옆으로 누움 (머리가 왼쪽)
            case 2: d = DotPeople.Draw(l, DotFacing.N, DotPose.Stand, 0).Rotate(cw: false); break;      // 엎드려 김
            default: d = DotPeople.Draw(l, f, p, frame); break;
        }
        var img = d.ToImg();
        var gimg = Image.CreateFromData(img.W, img.H, false, Image.Format.Rgba8, img.Px);
        gimg.GenerateMipmaps();
        var tex = ImageTexture.CreateFromImage(gimg);
        s = new DotSprite { Tex = tex, Crisp = new CanvasTexture { DiffuseTexture = tex, TextureFilter = TextureFilterEnum.Nearest }, Used = _dotFrame };
        _dotCache[key] = s;
        return s;
    }

    private Texture2D DotTex(DotSprite s) => Zoom >= 0.999f ? s.Crisp : s.Tex;

    /// <summary>건강 카드 그림: 서 있는 앞모습 (든 것 없이) — 가장 가까운 칸으로 또렷하게. 아직 못 그렸으면 null.</summary>
    public Texture2D? Portrait(CrewMember c, out PersonLook look)
    {
        var spec = Puppet.Of(_world, c);
        look = DotLookOf(c, spec);
        look.Held = DotHeld.None;
        look.EyesClosed = c.Dead || c.Down;
        if (_dotBudget <= 0) _dotBudget = 1; // HUD는 다른 때 그린다 — 한 장은 늘 허락
        return DotGet(look, DotSig(look), DotFacing.S, DotPose.Stand, 0, 0)?.Crisp;
    }

    /// <summary>사람 위치에서 머리 위까지 (말풍선 · 상태 딱지 자리). 도트 사람은 한 칸 키라 예전 인형보다 높다.</summary>
    public Vector2 HeadLift(CrewMember c, float puppetLift)
    {
        if (!DotCrew || ZoomDetail.Shows(Zoom, Detail.CrewDot) || c.Dead || c.Down || c.Pose == Pose.Sleeping) return new Vector2(0f, puppetLift);
        float lift = c.IsChild ? -15f : c.Age < 19f ? -22f : -25f;
        if (c.Pose == Pose.Sitting) lift += 3f;
        return new Vector2(0f, lift);
    }

    private static DotFacing DotFace(Vector2 facing)
    {
        if (MathF.Abs(facing.X) > MathF.Abs(facing.Y) * 1.15f) return facing.X > 0 ? DotFacing.E : DotFacing.W;
        return facing.Y < 0 ? DotFacing.N : DotFacing.S;
    }

    /// <summary>서 있는 · 걷는 · 앉은 · 일하는 사람 (발이 at). 그렸으면 true.</summary>
    private bool PaintDotStanding(CanvasItem ci, CrewMember c, Vector2 at, Vector2 facing, PuppetSpec spec)
    {
        var l = DotLookOf(c, spec);
        ulong sig = DotSig(l);
        var f = DotFace(facing);
        var pose = spec.Pose switch
        {
            PuppetPose.Walk or PuppetPose.Run => DotPose.Walk,
            PuppetPose.Sit => DotPose.Sit,
            PuppetPose.Kneel => DotPose.Kneel,
            PuppetPose.Work => DotPose.Work,
            _ => DotPose.Stand,
        };
        if (spec.TwoHands && pose is DotPose.Stand or DotPose.Walk) pose = DotPose.Carry;
        int frame = 0;
        if (pose == DotPose.Walk) frame = (int)(_time * (spec.Pose == PuppetPose.Run ? 12f : 7f) + c.Id * 0.37f) & 3;
        var s = DotGet(l, sig, f, pose, frame, 0) ?? (frame != 0 ? DotGet(l, sig, f, pose, 0, 0) : null);
        if (s == null) return false;
        var o = new Vector2(Mathf.Round(at.X) - DotPeople.Cx, Mathf.Round(at.Y) - DotPeople.Foot);
        ci.DrawTextureRect(DotTex(s), new Rect2(o, DotPeople.FW, DotPeople.FH), false);
        return true;
    }

    /// <summary>누운 사람 (쓰러짐 · 숨짐 · 업혀 감 · 기어감) — 가운데가 at. 그렸으면 true.</summary>
    private bool PaintDotLying(CanvasItem ci, CrewMember c, Vector2 at, bool crawl)
    {
        var spec = Puppet.Of(_world, c);
        var l = DotLookOf(c, spec);
        l.Held = DotHeld.None;
        if (crawl) l.EyesClosed = false;
        var s = DotGet(l, DotSig(l), DotFacing.S, DotPose.Stand, 0, crawl ? (byte)2 : (byte)1);
        if (s == null) return false;
        var o = new Vector2(Mathf.Round(at.X) - DotPeople.FH / 2f, Mathf.Round(at.Y) - DotPeople.FW / 2f);
        ci.DrawTextureRect(DotTex(s), new Rect2(o, DotPeople.FH, DotPeople.FW), false);
        return true;
    }

    /// <summary>침대에서 자는 사람: 베개 위 머리(눈 감음) + 그 아래 담요 (개인 색).</summary>
    private bool PaintDotSleeping(CanvasItem ci, CrewMember c, Vector2 at)
    {
        var spec = Puppet.Of(_world, c);
        var l = DotLookOf(c, spec);
        l.Held = DotHeld.None;
        var s = DotGet(l, DotSig(l), DotFacing.S, DotPose.Stand, 0, 0);
        if (s == null) return false;
        int headRows = 20;
        var o = new Vector2(Mathf.Round(at.X) - DotPeople.Cx, Mathf.Round(at.Y) - 14f);
        ci.DrawTextureRectRegion(DotTex(s), new Rect2(o, DotPeople.FW, headRows), new Rect2(0, 0, DotPeople.FW, headRows));
        // 담요: 접힌 끝(밝게) · 몸 윤곽(가운데 어둡게) · 외곽선
        var pc = Palette.Crew(c.Id);
        var blanket = pc.Darkened(0.25f);
        float bx = Mathf.Round(at.X) - 11f, by = o.Y + headRows - 4f;
        ci.DrawRect(new Rect2(bx - 1, by - 1, 24, 20), new Color(0.04f, 0.05f, 0.08f, 0.9f));
        ci.DrawRect(new Rect2(bx, by, 22, 18), blanket);
        ci.DrawRect(new Rect2(bx, by, 22, 3), pc.Lightened(0.15f));
        ci.DrawRect(new Rect2(bx + 5, by + 5, 12, 12), blanket.Darkened(0.12f));
        ci.DrawRect(new Rect2(bx, by + 17, 22, 1), blanket.Darkened(0.35f));
        return true;
    }
}
