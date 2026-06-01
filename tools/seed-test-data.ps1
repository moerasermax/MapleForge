# 種測試資料：testuser 帳號(BCrypt) + TestHero 角色，直接寫 LiteDB。
# 取代 create-test-char.ps1 的「帳號必須先存在」限制：本腳本連帳號一起建，不需先跑 server/登入。
# 用 server 自己的 BCrypt.Net-Next + LiteDB 組件，保證雜湊與 schema 一致。
# 純 PowerShell 操作 LiteDB（不編譯 C#，避免 .NET10 vs netstandard 參考衝突）；
# BsonValue 以 [LiteDB.BsonValue] 轉型（PowerShell cast 會套用 implicit operator）。
param(
    [string]$Root = "D:\WorkSpace\AI_Lab\研究中\MapleStory\V113\MapleForge",
    [string]$AccountName = "testuser",
    [string]$Password = "test1234",
    [string]$CharacterName = "TestHero",
    [int]$MapId = 100000000,
    [int]$SpawnPoint = 0,
    [int]$Level = 1,
    [int]$Job = 0
)
$ErrorActionPreference = "Stop"

$bin = Join-Path $Root "src\Maple.Host.Login\bin\Debug\net10.0"
$liteDb = Join-Path $bin "LiteDB.dll"
$bcrypt = Join-Path $bin "BCrypt.Net-Next.dll"
foreach ($d in @($liteDb, $bcrypt)) {
    if (!(Test-Path -LiteralPath $d)) { throw "找不到組件 $d，請先 dotnet build。" }
}

# db 路徑（與 server appsettings 一致）
$settings = Get-Content -LiteralPath (Join-Path $Root "src\Maple.Host.Login\appsettings.json") -Raw | ConvertFrom-Json
$inst = [string]$settings.Instance.Name; if ([string]::IsNullOrWhiteSpace($inst)) { $inst = "MapleForge" }
$dataDir = [string]$settings.Instance.DataDirectory; if ([string]::IsNullOrWhiteSpace($dataDir)) { $dataDir = "data" }
$dbDir = if ([System.IO.Path]::IsPathRooted($dataDir)) { $dataDir } else { Join-Path $Root $dataDir }
if (!(Test-Path -LiteralPath $dbDir)) { New-Item -ItemType Directory -Path $dbDir -Force | Out-Null }
$dbPath = Join-Path $dbDir ($inst + ".db")

Add-Type -Path $bcrypt
Add-Type -Path $liteDb
$hash = [BCrypt.Net.BCrypt]::HashPassword($Password, 12)
$acctName = $AccountName.Trim().ToLowerInvariant()

function BV($v) { [LiteDB.BsonValue]$v }   # 顯式轉 BsonValue
function NextIntId($col) {                 # 取整數 _id 的下一個值（untyped 預設 ObjectId，須自行給 int 對齊 server 的 int Id）
    $m = 0
    foreach ($d in $col.FindAll()) { if ($d["_id"].IsInt32 -and $d["_id"].AsInt32 -gt $m) { $m = $d["_id"].AsInt32 } }
    return $m + 1
}

$db = New-Object LiteDB.LiteDatabase($dbPath)
try {
    $accounts = $db.GetCollection("accounts")
    # 不在此建唯一索引：server 的 LiteDbAccountRepository 啟動時會 EnsureIndex；此處先 FindOne 防重複即可
    $acc = $accounts.FindOne([LiteDB.Query]::EQ("AccountName", (BV $acctName)))
    if ($null -eq $acc) {
        $accountId = NextIntId $accounts
        $acc = New-Object LiteDB.BsonDocument
        $acc["_id"]          = BV $accountId          # 顯式 int _id，對齊 server 的 Account.Id (int)
        $acc["AccountName"] = BV $acctName
        $acc["PasswordHash"] = BV $hash
        $acc["CreatedAt"]    = BV ([DateTime]::UtcNow)
        $acc["IsBanned"]     = BV $false
        $acc["BanReason"]    = BV ""
        [void]$accounts.Insert($acc)
        Write-Host "[seed] 建立帳號 $acctName (id=$accountId)" -ForegroundColor Green
    } else {
        $accountId = $acc["_id"].AsInt32                # 慣例：Id 映射為 _id
        $acc["PasswordHash"] = BV $hash                 # 重設為已知密碼，確保可登入
        $acc["IsBanned"] = BV $false
        [void]$accounts.Update($acc)
        Write-Host "[seed] 帳號已存在 $acctName (id=$accountId)，密碼已校正" -ForegroundColor Yellow
    }

    $chars = $db.GetCollection("characters")
    $existing = $chars.FindOne([LiteDB.Query]::And(
        [LiteDB.Query]::EQ("AccountId", (BV $accountId)),
        [LiteDB.Query]::EQ("Name", (BV $CharacterName))))
    if ($null -eq $existing) {
        $stats = New-Object LiteDB.BsonDocument
        $stats["Str"]=BV 12; $stats["Dex"]=BV 5; $stats["Int"]=BV 4; $stats["Luk"]=BV 4
        $stats["Hp"]=BV 50; $stats["MaxHp"]=BV 50; $stats["Mp"]=BV 5; $stats["MaxMp"]=BV 5
        $charId = NextIntId $chars
        $doc = New-Object LiteDB.BsonDocument
        $doc["_id"]=BV $charId                        # 顯式 int _id，對齊 server 的 Character.Id (int)
        $doc["AccountId"]=BV $accountId; $doc["Name"]=BV $CharacterName; $doc["Gender"]=BV 0; $doc["SkinColor"]=BV 0
        $doc["Face"]=BV 20000; $doc["Hair"]=BV 30000; $doc["Level"]=BV $Level; $doc["Job"]=BV $Job
        $doc["Stats"]=$stats
        $doc["RemainingAp"]=BV 0; $doc["RemainingSp"]=BV 0; $doc["Exp"]=BV 0; $doc["Fame"]=BV 0; $doc["GachExp"]=BV 0
        $doc["MapId"]=BV $MapId; $doc["SpawnPoint"]=BV $SpawnPoint; $doc["Equips"]=(New-Object LiteDB.BsonArray)
        [void]$chars.Insert($doc)
        Write-Host "[seed] 建立角色 $CharacterName (acct=$accountId, map=$MapId)" -ForegroundColor Green
    } else {
        $existing["MapId"]=BV $MapId; $existing["SpawnPoint"]=BV $SpawnPoint; $existing["Level"]=BV $Level; $existing["Job"]=BV $Job
        [void]$chars.Update($existing)
        Write-Host "[seed] 角色已存在 $CharacterName，已校正地圖/等級" -ForegroundColor Yellow
    }
} finally { $db.Dispose() }
Write-Host "[seed-test-data] 完成：$dbPath" -ForegroundColor Cyan
exit 0
