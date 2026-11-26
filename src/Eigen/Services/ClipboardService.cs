using System.Collections.Specialized;
using System.IO;
using System.Windows;

namespace Eigen.Services;

public static class ClipboardService
{
    private const int DropEffectCopy = 1;
    private const int DropEffectMove = 2;

    public static void CopyFilesToClipboard(IReadOnlyList<string> paths, bool isCut)
    {
        if (paths.Count == 0) return;

        var dropList = new StringCollection();
        foreach (var path in paths)
            dropList.Add(path);

        var dataObject = new DataObject();
        dataObject.SetFileDropList(dropList);

        var effectBytes = BitConverter.GetBytes(isCut ? DropEffectMove : DropEffectCopy);
        var stream = new MemoryStream(effectBytes);
        dataObject.SetData("Preferred DropEffect", stream);

        Clipboard.SetDataObject(dataObject, true);
    }

    public static (IReadOnlyList<string> Paths, bool IsCut) GetClipboardFiles()
    {
        if (!Clipboard.ContainsFileDropList())
            return ([], false);

        var list = Clipboard.GetFileDropList();
        var paths = new List<string>(list.Count);
        foreach (var item in list)
        {
            if (!string.IsNullOrWhiteSpace(item))
                paths.Add(item);
        }

        var isCut = false;
        try
        {
            if (Clipboard.GetDataObject() is IDataObject dataObj && dataObj.GetDataPresent("Preferred DropEffect"))
            {
                if (dataObj.GetData("Preferred DropEffect") is MemoryStream ms && ms.Length >= 4)
                {
                    var buffer = new byte[4];
                    ms.Position = 0;
                    _ = ms.Read(buffer, 0, 4);
                    var effect = BitConverter.ToInt32(buffer, 0);
                    isCut = (effect & DropEffectMove) == DropEffectMove;
                }
            }
        }
        catch
        {
            isCut = false;
        }

        return (paths, isCut);
    }

    public static bool HasClipboardFiles() => Clipboard.ContainsFileDropList();

    public static void CopyText(string text)
    {
        if (!string.IsNullOrEmpty(text))
            Clipboard.SetText(text);
    }
}
