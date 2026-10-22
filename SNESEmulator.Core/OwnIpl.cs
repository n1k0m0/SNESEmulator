namespace SNESEmulator.Core;

/// <summary>Own open boot program, assembled from tools/own-ipl.py. Compatible upload protocol, different timing from Nintendo IPL.</summary>
public static class OwnIpl
{
    public static byte[] Bytes=>Convert.FromHexString(Hex);
    public const string Hex="20CDEFBDE800C61DD0FC8FAAF48FBBF578CCF4D0FBBAF6DA00BAF4C4F4DD5DF019EBF4D0FC7EF4D00DE4F5D700CBF4FCD0F3AB012FEF30DD2FEB1F000000C0FF";
}
