namespace Kms.Input.Counting;

/// <summary>按键语义分组（v3 <c>KeySemantics</c> 的移植）。</summary>
public static class KeySemantics
{
    public static readonly string[] Names =
    [
        "移动类键位", "数字 / 技能栏", "交互类键位", "功能 / 修饰键", "文字及其他", "未归位记录"
    ];

    /// <summary>0=移动 1=数字 2=交互 3=功能/修饰 4=文字及其他。</summary>
    public static int Group(int virtualKey)
    {
        if (virtualKey is 65 or 68 or 83 or 87 or (>= 37 and <= 40))
        {
            return 0;
        }

        if (virtualKey is (>= 48 and <= 57) or (>= 96 and <= 105))
        {
            return 1;
        }

        if (virtualKey is 32 or 16 or 160 or 161)
        {
            return 2;
        }

        if (virtualKey is 17 or 18 or (>= 162 and <= 165) or 91 or 92 or (>= 112 and <= 135) or 9 or 27)
        {
            return 3;
        }

        return 4;
    }
}
