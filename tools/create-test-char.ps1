param(
    [string]$Root = "D:\WorkSpace\AI_Lab\研究中\MapleStory\V113\MapleForge",
    [string]$AccountName = "testuser",
    [string]$CharacterName = "TestHero",
    [int]$MapId = 100000000,
    [int]$SpawnPoint = 0,
    [int]$Level = 1,
    [int]$Job = 0
)

$ErrorActionPreference = "Stop"

function Resolve-LiteDbAssemblyPath {
    param([string]$ProjectRoot)

    $candidates = @(
        (Join-Path $ProjectRoot "src\Maple.Host.Login\bin\Debug\net10.0\LiteDB.dll"),
        (Join-Path $ProjectRoot "src\Maple.Host.Login\bin\Release\net10.0\LiteDB.dll"),
        (Join-Path $env:USERPROFILE ".nuget\packages\litedb\5.0.21\lib\netstandard2.0\LiteDB.dll")
    )

    foreach ($path in $candidates) {
        if (Test-Path -LiteralPath $path) { return $path }
    }

    throw "找不到 LiteDB.dll，請先執行一次 dotnet build。"
}

$settingsPath = Join-Path $Root "src\Maple.Host.Login\appsettings.json"
if (!(Test-Path -LiteralPath $settingsPath)) {
    throw "找不到設定檔: $settingsPath"
}

$settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
$instanceName = [string]$settings.Instance.Name
$dataDirectory = [string]$settings.Instance.DataDirectory
if ([string]::IsNullOrWhiteSpace($instanceName)) { $instanceName = "MapleForge" }
if ([string]::IsNullOrWhiteSpace($dataDirectory)) { $dataDirectory = "data" }
if ([System.IO.Path]::IsPathRooted($dataDirectory)) {
    $dbDir = $dataDirectory
} else {
    $dbDir = Join-Path $Root $dataDirectory
}
$dbPath = Join-Path $dbDir ($instanceName + ".db")
if (!(Test-Path -LiteralPath $dbPath)) {
    throw "找不到資料庫: $dbPath`n請先啟動一次 server 並用 testuser 登入建立帳號。"
}

$liteDbDll = Resolve-LiteDbAssemblyPath -ProjectRoot $Root
Add-Type -Path $liteDbDll

$csharp = @"
using System;
using LiteDB;

public static class TestCharSeeder
{
    public static string Ensure(string dbPath, string accountName, string characterName, int mapId, byte spawnPoint, byte level, short job)
    {
        using var db = new LiteDatabase(dbPath);
        var accounts = db.GetCollection("accounts");
        var chars = db.GetCollection("characters");

        accountName = accountName.Trim().ToLowerInvariant();
        var account = accounts.FindOne(Query.EQ("AccountName", accountName));
        if (account == null)
            return "ACCOUNT_NOT_FOUND";

        int accountId = account["Id"].AsInt32;
        var existing = chars.FindOne(Query.And(
            Query.EQ("AccountId", accountId),
            Query.EQ("Name", characterName)
        ));

        if (existing != null)
        {
            existing["MapId"] = mapId;
            existing["SpawnPoint"] = spawnPoint;
            existing["Level"] = level;
            existing["Job"] = job;
            chars.Update(existing);
            return "UPDATED";
        }

        var doc = new BsonDocument
        {
            ["AccountId"] = accountId,
            ["Name"] = characterName,
            ["Gender"] = 0,
            ["SkinColor"] = 0,
            ["Face"] = 20000,
            ["Hair"] = 30000,
            ["Level"] = level,
            ["Job"] = job,
            ["Stats"] = new BsonDocument
            {
                ["Str"] = 12, ["Dex"] = 5, ["Int"] = 4, ["Luk"] = 4,
                ["Hp"] = 50, ["MaxHp"] = 50, ["Mp"] = 5, ["MaxMp"] = 5
            },
            ["RemainingAp"] = 0,
            ["RemainingSp"] = 0,
            ["Exp"] = 0,
            ["Fame"] = 0,
            ["GachExp"] = 0,
            ["MapId"] = mapId,
            ["SpawnPoint"] = spawnPoint,
            ["Equips"] = new BsonArray()
        };
        chars.Insert(doc);
        return "CREATED";
    }
}
"@

Add-Type -TypeDefinition $csharp -ReferencedAssemblies $liteDbDll

$result = [TestCharSeeder]::Ensure(
    $dbPath,
    $AccountName,
    $CharacterName,
    $MapId,
    [byte]$SpawnPoint,
    [byte]$Level,
    [int16]$Job
)

switch ($result) {
    "CREATED" {
        Write-Host "[create-test-char] 已建立角色 '$CharacterName'（帳號=$AccountName, map=$MapId, spawn=$SpawnPoint, lv=$Level, job=$Job）" -ForegroundColor Green
        exit 0
    }
    "UPDATED" {
        Write-Host "[create-test-char] 已更新既有角色 '$CharacterName' 的地圖/出生點/等級/職業" -ForegroundColor Yellow
        exit 0
    }
    "ACCOUNT_NOT_FOUND" {
        Write-Host "[create-test-char] 找不到帳號 '$AccountName'。請先以該帳號登入一次（autoRegister）後再執行。" -ForegroundColor Red
        exit 2
    }
    default {
        Write-Host "[create-test-char] 未知結果: $result" -ForegroundColor Red
        exit 3
    }
}
