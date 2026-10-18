using SparseTypes;

namespace SparseTypes.Tests;

// P=K=64 constructor-bound immutable model: guards the single-pass
// name index in ModelConstructorBinding and the bound-name set in
// SparseModelDiscovery.GetMembers.
[SparseModel]
public partial class WideImmutableModel(
    int p00,
    int p01,
    int p02,
    int p03,
    int p04,
    int p05,
    int p06,
    int p07,
    int p08,
    int p09,
    int p10,
    int p11,
    int p12,
    int p13,
    int p14,
    int p15,
    int p16,
    int p17,
    int p18,
    int p19,
    int p20,
    int p21,
    int p22,
    int p23,
    int p24,
    int p25,
    int p26,
    int p27,
    int p28,
    int p29,
    int p30,
    int p31,
    int p32,
    int p33,
    int p34,
    int p35,
    int p36,
    int p37,
    int p38,
    int p39,
    int p40,
    int p41,
    int p42,
    int p43,
    int p44,
    int p45,
    int p46,
    int p47,
    int p48,
    int p49,
    int p50,
    int p51,
    int p52,
    int p53,
    int p54,
    int p55,
    int p56,
    int p57,
    int p58,
    int p59,
    int p60,
    int p61,
    int p62,
    int p63
)
{
    public int P00 { get; } = p00;
    public int P01 { get; } = p01;
    public int P02 { get; } = p02;
    public int P03 { get; } = p03;
    public int P04 { get; } = p04;
    public int P05 { get; } = p05;
    public int P06 { get; } = p06;
    public int P07 { get; } = p07;
    public int P08 { get; } = p08;
    public int P09 { get; } = p09;
    public int P10 { get; } = p10;
    public int P11 { get; } = p11;
    public int P12 { get; } = p12;
    public int P13 { get; } = p13;
    public int P14 { get; } = p14;
    public int P15 { get; } = p15;
    public int P16 { get; } = p16;
    public int P17 { get; } = p17;
    public int P18 { get; } = p18;
    public int P19 { get; } = p19;
    public int P20 { get; } = p20;
    public int P21 { get; } = p21;
    public int P22 { get; } = p22;
    public int P23 { get; } = p23;
    public int P24 { get; } = p24;
    public int P25 { get; } = p25;
    public int P26 { get; } = p26;
    public int P27 { get; } = p27;
    public int P28 { get; } = p28;
    public int P29 { get; } = p29;
    public int P30 { get; } = p30;
    public int P31 { get; } = p31;
    public int P32 { get; } = p32;
    public int P33 { get; } = p33;
    public int P34 { get; } = p34;
    public int P35 { get; } = p35;
    public int P36 { get; } = p36;
    public int P37 { get; } = p37;
    public int P38 { get; } = p38;
    public int P39 { get; } = p39;
    public int P40 { get; } = p40;
    public int P41 { get; } = p41;
    public int P42 { get; } = p42;
    public int P43 { get; } = p43;
    public int P44 { get; } = p44;
    public int P45 { get; } = p45;
    public int P46 { get; } = p46;
    public int P47 { get; } = p47;
    public int P48 { get; } = p48;
    public int P49 { get; } = p49;
    public int P50 { get; } = p50;
    public int P51 { get; } = p51;
    public int P52 { get; } = p52;
    public int P53 { get; } = p53;
    public int P54 { get; } = p54;
    public int P55 { get; } = p55;
    public int P56 { get; } = p56;
    public int P57 { get; } = p57;
    public int P58 { get; } = p58;
    public int P59 { get; } = p59;
    public int P60 { get; } = p60;
    public int P61 { get; } = p61;
    public int P62 { get; } = p62;
    public int P63 { get; } = p63;
}

public sealed class WideImmutableBindingTests
{
    [Test]
    public void WideImmutableModel_RoundTripsAllBoundProperties()
    {
        var model = new WideImmutableModel(
            0,
            1,
            2,
            3,
            4,
            5,
            6,
            7,
            8,
            9,
            10,
            11,
            12,
            13,
            14,
            15,
            16,
            17,
            18,
            19,
            20,
            21,
            22,
            23,
            24,
            25,
            26,
            27,
            28,
            29,
            30,
            31,
            32,
            33,
            34,
            35,
            36,
            37,
            38,
            39,
            40,
            41,
            42,
            43,
            44,
            45,
            46,
            47,
            48,
            49,
            50,
            51,
            52,
            53,
            54,
            55,
            56,
            57,
            58,
            59,
            60,
            61,
            62,
            63
        );
        var restored = WideImmutableModel.Fragment.From(model).ToModel();
        for (var index = 0; index < 64; index++)
        {
            var name = "P" + index.ToString("00");
            var value = (int)typeof(WideImmutableModel).GetProperty(name)!.GetValue(restored)!;
            value.ShouldBe(index);
        }
        var clone = model.DeepClone();
        ReferenceEquals(clone, model).ShouldBeFalse();
        ((int)typeof(WideImmutableModel).GetProperty("P63")!.GetValue(clone)!).ShouldBe(63);
    }
}
