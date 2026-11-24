using MultiEmulator.Spectrum.Poke;
using MultiEmulator.Spectrum.Services;
using MultiEmulator.Spectrum.Tape;

namespace MultiEmulator.Spectrum.Media;

public static class MediaLoader
{
    public static void OpenPath(
        SpectrumMachine machine,
        string path,
        RomMediaFormat format = RomMediaFormat.Auto)
    {
        // TAP con POKE/DATA antes del USR (p.ej. Flying Shark BioToxin): en Auto/TZX
        // preferir .tzx.zip hermano si existe. Con formato TAP forzado, cargar el TAP
        // (intérprete BASIC + trampas ROM).
        if (format != RomMediaFormat.Tap && TryPreferTzxSibling(path, out string tzxSibling))
            path = tzxSibling;

        if (ArchiveMedia.IsArchive(path))
        {
            var (name, data) = ArchiveMedia.ExtractPreferredMediaDeep(path);
            string virtualPath = Path.Combine(Path.GetDirectoryName(path) ?? "", name);
            OpenBytes(machine, data, name, virtualPath);
            return;
        }

        var fileData = File.ReadAllBytes(path);
        OpenBytes(machine, fileData, Path.GetFileName(path), path);
    }

    /// <summary>
    /// Si <paramref name="path"/> es .tap/.tap.zip cuyo BASIC requiere intérprete
    /// y existe el .tzx hermano, devuelve ese TZX.
    /// </summary>
    public static bool TryPreferTzxSibling(string path, out string tzxPath)
    {
        tzxPath = path;
        string? sibling = null;
        if (path.EndsWith(".tap.zip", StringComparison.OrdinalIgnoreCase))
            sibling = path[..^".tap.zip".Length] + ".tzx.zip";
        else if (path.EndsWith(".tap", StringComparison.OrdinalIgnoreCase))
            sibling = Path.ChangeExtension(path, ".tzx");

        if (sibling == null || !File.Exists(sibling))
            return false;

        if (!TapPathRequiresInterpreter(path))
            return false;

        tzxPath = sibling;
        return true;
    }

    /// <summary>
    /// True si el TAP (o .tap.zip) tiene BASIC con POKE/DATA/READ antes del USR
    /// (carga instantánea flash+USR fallará; hace falta intérprete o TZX hermano).
    /// </summary>
    public static bool TapPathRequiresInterpreter(string path)
    {
        try
        {
            byte[] tapBytes;
            string name;
            if (ArchiveMedia.IsArchive(path) || path.EndsWith(".tap.zip", StringComparison.OrdinalIgnoreCase))
                (name, tapBytes) = ArchiveMedia.ExtractPreferredMediaDeep(path);
            else
            {
                name = Path.GetFileName(path);
                tapBytes = File.ReadAllBytes(path);
            }

            return TapBytesRequireInterpreter(name, tapBytes);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Igual que <see cref="TapPathRequiresInterpreter"/> sobre bytes ya descargados
    /// (p.ej. .tap.zip en memoria antes de aceptar la URL del catálogo).
    /// </summary>
    public static bool TapArchiveBytesRequireInterpreter(byte[] archiveOrTapBytes, string fileNameHint)
    {
        try
        {
            string hint = fileNameHint ?? "";
            if (hint.EndsWith(".tap.zip", StringComparison.OrdinalIgnoreCase)
                || hint.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                || LooksLikeZip(archiveOrTapBytes))
            {
                string temp = Path.Combine(
                    Path.GetTempPath(),
                    "me-tapchk-" + Guid.NewGuid().ToString("N") + ".zip");
                try
                {
                    File.WriteAllBytes(temp, archiveOrTapBytes);
                    var (name, data) = ArchiveMedia.ExtractPreferredMediaDeep(temp);
                    return TapBytesRequireInterpreter(name, data);
                }
                finally
                {
                    try { File.Delete(temp); } catch { /* ignore */ }
                }
            }

            return TapBytesRequireInterpreter(hint, archiveOrTapBytes);
        }
        catch
        {
            return false;
        }
    }

    private static bool TapBytesRequireInterpreter(string name, byte[] tapBytes)
    {
        if (!name.EndsWith(".tap", StringComparison.OrdinalIgnoreCase))
            return false;

        var tape = TapeImage.LoadTap(tapBytes);
        for (int i = 0; i + 1 < tape.Blocks.Count; i++)
        {
            var h = tape.Blocks[i];
            var d = tape.Blocks[i + 1];
            if (h.Flag != 0x00 || h.Data.Length < 19 || h.Data[1] != 0)
                continue;
            if (d.Flag != 0xFF || d.Data.Length < 2)
                continue;
            return TapeLoader.BasicRequiresInterpreterBeforeUsr(d.Data);
        }

        return false;
    }

    private static bool LooksLikeZip(byte[] bytes) =>
        bytes.Length >= 4
        && bytes[0] == (byte)'P'
        && bytes[1] == (byte)'K';

    public static void OpenBytes(SpectrumMachine machine, byte[] data, string fileName, string? sourcePath = null)
    {
        string ext = Path.GetExtension(fileName).ToLowerInvariant();
        sourcePath ??= fileName;

        switch (ext)
        {
            case ".z80":
            case ".sna":
                machine.TrDos.Eject();
                machine.LoadSnapshot(data, ext);
                break;
            case ".tap":
            {
                var tape = TapeImage.LoadTap(data);
                tape.SourcePath = sourcePath;
                machine.InsertTape(tape);
                AutoStartTape(machine);
                break;
            }
            case ".tzx":
            {
                var tape = TapeImage.LoadTzx(data);
                tape.SourcePath = sourcePath;
                machine.InsertTape(tape);
                AutoStartTape(machine);
                break;
            }
            case ".trd":
                machine.LoadTrd(data);
                break;
            case ".dsk":
                throw new NotSupportedException(
                    "Formato DSK (+3) descargado, pero la carga de disco +3 aún no está implementada.");
            case ".rom":
            {
                machine.TrDos.Eject();
                if (data.Length >= 0x8000)
                {
                    machine.LoadSystemRoms(null, data);
                    machine.SetMachine(MachineType.Spectrum128);
                }
                else
                {
                    machine.LoadSystemRoms(data, null);
                    machine.SetMachine(MachineType.Spectrum48);
                }
                break;
            }
            case ".pok":
            {
                var trainers = PokeService.ParsePok(System.Text.Encoding.UTF8.GetString(data));
                if (trainers.Count > 0)
                    machine.ApplyPokes(trainers[0].Entries);
                break;
            }
            default:
                throw new NotSupportedException($"Unsupported media: {ext}");
        }
    }

    private static void AutoStartTape(SpectrumMachine machine)
    {
        // Preferir carga instantánea (flash CODE + USR, o BASIC LINE-NEW).
        // Fallback LAST_K solo tras editor listo (evita Break/"?"L en la ©).
        machine.Tape.AutoLoad = false;
        machine.Tape.CancelAutoLoad();
        if (machine.Tape.Tape == null)
            return;

        if (machine.TryInstantTapeStart())
            return;

        // Instant falló (p.ej. turbo sin USR): reactivar cinta y LOAD "" tras el editor.
        if (machine.Tape.Tape != null)
            machine.Tape.Tape.IsPlaying = true;
        machine.EnsureBasicEditorReady();
        machine.Tape.AutoLoad = true;
        machine.Tape.BeginAutoLoadCommand();
    }
}
