using Godot;
using Microsoft.Data.Sqlite;
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

public partial class Candidate : Node
{
    private SqliteConnection? _store;
    private UiProbe _ui = null!;
    private Label _saveStatus = null!;
    private Godot.Timer _debounce = null!;

    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        string directory = System.Environment.GetEnvironmentVariable("MISTSEA_PROBE_OUTPUT") ?? OS.GetUserDataDir();
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "draft.sqlite");
        bool existed = File.Exists(path);
        try
        {
            _store = StorageHarness.Open(path, existed);
            if (!existed)
                StorageHarness.Execute(_store, null, "CREATE TABLE draft(id INTEGER PRIMARY KEY CHECK(id=1), revision INTEGER NOT NULL, body TEXT NOT NULL, hash TEXT NOT NULL); INSERT INTO draft VALUES(1,0,'',''); PRAGMA user_version=1;");
            using var read = _store.CreateCommand();
            read.CommandText = "SELECT body,hash,revision FROM draft WHERE id=1;";
            using var row = read.ExecuteReader();
            if (!row.Read()) throw new InvalidDataException("Draft record missing; refusing to reset the file.");
            string body = row.GetString(0), hash = row.GetString(1);
            long revision = row.GetInt64(2);
            row.Close();
            if (revision > 0 && Hash(body) != hash) throw new InvalidDataException("Draft checksum mismatch; refusing to reset the file.");
            if (args.Contains("--candidate-save"))
            {
                Save(UiProbe.LongSample());
                GD.Print("MISTSEA_CANDIDATE_RESULTS " + JsonSerializer.Serialize(new { saved = true, path, provider = typeof(SqliteConnection).Assembly.FullName, network_provider_configured = false }));
                GetTree().Quit();
                return;
            }
            if (args.Contains("--candidate-read"))
            {
                if (body != UiProbe.LongSample() || revision <= 0) throw new InvalidDataException("Reopened draft differs from the saved fixture.");
                GD.Print("MISTSEA_CANDIDATE_RESULTS " + JsonSerializer.Serialize(new { reopened = true, revision, hash, path }));
                GetTree().Quit();
                return;
            }
            _ui = new UiProbe();
            AddChild(_ui);
            _ui.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            if (revision > 0) _ui.Editor.Text = body;
            _ui.Finish.Text = "保存当前正文";
            _saveStatus = new Label { Text = "本地草稿：" + (revision > 0 ? "已读取保存版本 " + revision : "尚未保存"), AccessibilityName = "本地保存状态" };
            _ui.AddFooter(_saveStatus);
            _debounce = new Godot.Timer { OneShot = true, WaitTime = 0.5 };
            AddChild(_debounce);
            _debounce.Timeout += PersistEditor;
            _ui.Editor.TextChanged += () => { _saveStatus.Text = "尚未保存"; _debounce.Start(); };
            _ui.Finish.Pressed += PersistEditor;
        }
        catch (Exception error)
        {
            GD.PrintErr("MISTSEA_CANDIDATE_READ_ERROR " + error);
            if (args.Any(a => a.StartsWith("--candidate-"))) { GetTree().Quit(4); return; }
            var label = new Label { Text = "读取失败，原文件已保留。\n" + error.Message, Position = new Vector2(24, 24) };
            AddChild(label);
        }
    }

    private static string Hash(string body) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
    private void Save(string body)
    {
        if (_store is null) throw new InvalidOperationException("Storage is not available.");
        using var tx = _store.BeginTransaction(deferred: false);
        using var command = _store.CreateCommand();
        command.Transaction = tx;
        command.CommandText = "UPDATE draft SET revision=revision+1,body=$body,hash=$hash WHERE id=1;";
        command.Parameters.AddWithValue("$body", body);
        command.Parameters.AddWithValue("$hash", Hash(body));
        if (command.ExecuteNonQuery() != 1) throw new InvalidDataException("Draft record missing.");
        tx.Commit();
    }
    private void PersistEditor()
    {
        try { Save(_ui.Editor.Text); _saveStatus.Text = "当前正文已保存到本机"; }
        catch (Exception error) { _saveStatus.Text = "保存失败，正文仍在编辑区：" + error.Message; }
    }
    public override void _ExitTree() => _store?.Dispose();
}
