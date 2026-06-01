// W2 — DTO serialisation round-trip tests
// Guards the IPC contract between the UI process and the elevated Helper.
// If any field is silently dropped or mis-typed across JSON serialisation, the Helper
// will receive a partially-populated WritePlanRequest and silently skip operations —
// potentially leaving the user's game install in a half-modified state.

using System.Text.Json;
using Xunit;
using EWSR_PMR_ModApp.Core.Elevation;

namespace EWSR_PMR_ModApp.Core.Tests.Elevation;

public class DtoSerializationTests
{
    // Use the same options as Helper.Program (WriteIndented doesn't affect parsing).
    private static readonly JsonSerializerOptions s_opts = new() { WriteIndented = true };

    private static T RoundTrip<T>(T value)
    {
        string json = JsonSerializer.Serialize(value, s_opts);
        return JsonSerializer.Deserialize<T>(json, s_opts)!;
    }

    // ── WritePlanRequest ──────────────────────────────────────────────────────

    [Fact]
    public void WritePlanRequest_FullyPopulated_AllFieldsSurviveRoundTrip()
    {
        var original = new WritePlanRequest
        {
            Operation     = WritePlanOperation.Install,
            DataRoot      = @"C:\Program Files\PMR\data",
            ModId         = "mod-abc-123",
            FilesToCopy   =
            [
                new FileCopySpec
                {
                    SourcePath         = @"C:\AppData\staging\s1\livery.dds",
                    RelativeTargetPath = "vehicles/car_a/livery.dds"
                },
                new FileCopySpec
                {
                    SourcePath         = @"C:\AppData\staging\s1\engine.wav",
                    RelativeTargetPath = "sounds/engine.wav"
                }
            ],
            FilesToBackup = ["vehicles/car_a/livery.dds"],
            FilesToDelete = ["vehicles/car_a/old_skin.dds"]
        };

        var rt = RoundTrip(original);

        Assert.Equal(original.Operation, rt.Operation);
        Assert.Equal(original.DataRoot,  rt.DataRoot);
        Assert.Equal(original.ModId,     rt.ModId);

        Assert.NotNull(rt.FilesToCopy);
        Assert.Equal(2, rt.FilesToCopy!.Count);
        Assert.Equal("vehicles/car_a/livery.dds",          rt.FilesToCopy[0].RelativeTargetPath);
        Assert.Equal(@"C:\AppData\staging\s1\livery.dds",  rt.FilesToCopy[0].SourcePath);
        Assert.Equal("sounds/engine.wav",                  rt.FilesToCopy[1].RelativeTargetPath);

        Assert.NotNull(rt.FilesToBackup);
        Assert.Equal("vehicles/car_a/livery.dds", Assert.Single(rt.FilesToBackup!));

        Assert.NotNull(rt.FilesToDelete);
        Assert.Equal("vehicles/car_a/old_skin.dds", Assert.Single(rt.FilesToDelete!));
    }

    [Fact]
    public void WritePlanRequest_NullableLists_NullRemainsNullAfterRoundTrip()
    {
        // Null lists must NOT be turned into empty lists — the executor logic
        // branches differently on null (skip) vs empty (iterate nothing).
        var original = new WritePlanRequest
        {
            Operation     = WritePlanOperation.Uninstall,
            DataRoot      = @"C:\PMR\data",
            ModId         = "mod-xyz",
            FilesToCopy   = null,
            FilesToBackup = null,
            FilesToDelete = null
        };

        var rt = RoundTrip(original);

        Assert.Equal(WritePlanOperation.Uninstall, rt.Operation);
        Assert.Null(rt.FilesToCopy);
        Assert.Null(rt.FilesToBackup);
        Assert.Null(rt.FilesToDelete);
    }

    [Fact]
    public void WritePlanRequest_ReapplyOperation_RoundTrips()
    {
        var original = new WritePlanRequest
        {
            Operation   = WritePlanOperation.Reapply,
            DataRoot    = @"C:\PMR\data",
            ModId       = "mod-reapply",
            FilesToCopy = [new FileCopySpec { SourcePath = @"C:\AppData\mods\x\f.dds", RelativeTargetPath = "vehicles/f.dds" }]
        };

        var rt = RoundTrip(original);

        Assert.Equal(WritePlanOperation.Reapply, rt.Operation);
        Assert.NotNull(rt.FilesToCopy);
        Assert.Single(rt.FilesToCopy!);
    }

    // ── WriteResult ───────────────────────────────────────────────────────────

    [Fact]
    public void WriteResult_WithErrors_AllFieldsSurviveRoundTrip()
    {
        var original = new WriteResult
        {
            Success       = false,
            ErrorMessage  = @"Access denied to C:\PMR\data",
            FilesCopied   = 2,
            FilesDeleted  = 0,
            FilesBackedUp = 1,
            Errors        =
            [
                new FileOperationError { RelativePath = "vehicles/car_a/livery.dds", Message = "Access denied" },
                new FileOperationError { RelativePath = "*",                          Message = "Unexpected exception" }
            ]
        };

        var rt = RoundTrip(original);

        Assert.False(rt.Success);
        Assert.Equal(@"Access denied to C:\PMR\data", rt.ErrorMessage);
        Assert.Equal(2, rt.FilesCopied);
        Assert.Equal(0, rt.FilesDeleted);
        Assert.Equal(1, rt.FilesBackedUp);
        Assert.Equal(2, rt.Errors.Count);
        Assert.Equal("vehicles/car_a/livery.dds", rt.Errors[0].RelativePath);
        Assert.Equal("Access denied",             rt.Errors[0].Message);
        Assert.Equal("*",                         rt.Errors[1].RelativePath);
    }

    [Fact]
    public void WriteResult_Success_NullErrorMessage_EmptyErrors_RoundTrips()
    {
        var original = new WriteResult
        {
            Success       = true,
            ErrorMessage  = null,
            FilesCopied   = 5,
            FilesDeleted  = 2,
            FilesBackedUp = 3
        };

        var rt = RoundTrip(original);

        Assert.True(rt.Success);
        Assert.Null(rt.ErrorMessage);
        Assert.Equal(5, rt.FilesCopied);
        Assert.Equal(2, rt.FilesDeleted);
        Assert.Equal(3, rt.FilesBackedUp);
        Assert.Empty(rt.Errors);
    }

    // ── FileCopySpec ──────────────────────────────────────────────────────────

    [Fact]
    public void FileCopySpec_RoundTrips()
    {
        var original = new FileCopySpec
        {
            SourcePath         = @"C:\AppData\EWSR_PMR_ModApp\staging\abc\livery.dds",
            RelativeTargetPath = "vehicles/car_a/livery.dds"
        };

        var rt = RoundTrip(original);

        Assert.Equal(original.SourcePath,         rt.SourcePath);
        Assert.Equal(original.RelativeTargetPath, rt.RelativeTargetPath);
    }

    // ── FileOperationError ────────────────────────────────────────────────────

    [Fact]
    public void FileOperationError_RoundTrips()
    {
        var original = new FileOperationError
        {
            RelativePath = "vehicles/car_a/livery.dds",
            Message      = "The process cannot access the file because it is being used by another process."
        };

        var rt = RoundTrip(original);

        Assert.Equal(original.RelativePath, rt.RelativePath);
        Assert.Equal(original.Message,      rt.Message);
    }

    // ── WritePlanOperation enum ───────────────────────────────────────────────

    [Theory]
    [InlineData(WritePlanOperation.Install)]
    [InlineData(WritePlanOperation.Uninstall)]
    [InlineData(WritePlanOperation.Reapply)]
    public void WritePlanOperation_AllValues_RoundTripViaRequest(WritePlanOperation op)
    {
        // Enum must survive serialisation as a numeric value and deserialise back correctly.
        var request = new WritePlanRequest { Operation = op, DataRoot = @"C:\PMR\data", ModId = "m" };
        var rt      = RoundTrip(request);
        Assert.Equal(op, rt.Operation);
    }
}
