using System.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Shell;
using FFXIVClientStructs.FFXIV.Component.Shell;

namespace XSZRemoteChatBridge;

public static unsafe class GameChatSender
{
    public static bool TrySendMessage(string message, bool saveToHistory, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(message))
            return false;

        try
        {
            SendMessage(Encoding.UTF8.GetBytes(message), saveToHistory);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static bool TrySendCommand(string command, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(command))
            return false;

        try
        {
            SendCommand(Encoding.UTF8.GetBytes(command));
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static string SanitiseText(string text)
    {
        using var utf8String = new Utf8String();
        utf8String.SetString(text);
        utf8String.SanitizeString((AllowedEntities)0x27F);
        return utf8String.ToString();
    }

    private static void SendMessage(ReadOnlySpan<byte> message, bool saveToHistory)
    {
        if (message.Length == 0)
            return;

        using var builder = new RentedSeStringBuilder();
        var encoded = builder.Builder
            .Append(message)
            .ToReadOnlySeString()
            .ToDalamudString()
            .EncodeWithNullTerminator();

        var utf8String = Utf8String.FromSequence(encoded);
        try
        {
            var uiModule = UIModule.Instance();
            if (uiModule == null)
                throw new InvalidOperationException("UIModule unavailable");

            uiModule->ProcessChatBoxEntry(utf8String, (nint)utf8String, saveToHistory);
        }
        finally
        {
            utf8String->Dtor(true);
        }
    }

    private static void SendCommand(ReadOnlySpan<byte> command)
    {
        if (command.Length == 0)
            return;

        using var builder = new RentedSeStringBuilder();
        var encoded = builder.Builder
            .Append(command)
            .ToReadOnlySeString()
            .ToDalamudString()
            .EncodeWithNullTerminator();

        var utf8String = Utf8String.FromSequence(encoded);
        try
        {
            var uiModule = UIModule.Instance();
            if (uiModule == null)
                throw new InvalidOperationException("UIModule unavailable");

            var shellModule = (ShellCommandModule*)RaptureShellModule.Instance();
            if (shellModule == null)
                throw new InvalidOperationException("RaptureShellModule unavailable");

            shellModule->ExecuteCommandInner(utf8String, uiModule);
        }
        finally
        {
            utf8String->Dtor(true);
        }
    }
}
