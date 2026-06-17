namespace Maple.Adapters.V113.Login;

/// <summary>v113 接收 opcode（來源 recv.properties）。</summary>
internal static class V113RecvOp
{
    public const short LoginPassword       = 0x01;
    public const short ServerlistRequest   = 0x03;
    public const short CharlistRequest     = 0x04;
    public const short CharSelect          = 0x06;
    public const short CheckCharName       = 0x08;
    public const short CreateChar          = 0x0B;
    public const short ServerStatusRequest = 0x18;
    public const short DeleteChar          = 0x0D;
    public const short SetGender           = 0x19;  // c2s：帳號名+第二密碼(PIN)+gender byte
    public const short Pong                = 0x0E;
}

/// <summary>v113 送出 opcode（來源 send.properties）。</summary>
internal static class V113SendOp
{
    public const short LoginStatus    = 0x00;
    public const short Serverlist     = 0x02;
    public const short Charlist       = 0x03;
    public const short ServerIp       = 0x04;
    public const short CharNameResponse = 0x05;
    public const short AddNewCharEntry  = 0x06;
    public const short Ping           = 0x09;
    public const short SecondPwError  = 0x10;  // 第二密碼錯誤（保留，對照 Java）
    public const short ChooseGender   = 0x14;  // 要求客戶端選擇性別+設 PIN
    public const short GenderSet      = 0x15;  // 性別/PIN 設定完成確認
    public const short Serverstatus   = 0x16;
    public const short DeleteCharResponse = 0x07;
}
