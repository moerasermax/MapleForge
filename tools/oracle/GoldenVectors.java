import tools.MapleAESOFB;

/**
 * 黃金真值預言機：用「舊 Java cipher」(build/classes 內的 tools.MapleAESOFB)
 * 對固定輸入產生 byte 級參考輸出，供 C# 移植版逐 byte 比對。
 * 舊 Java 當年能與真客戶端通訊 → 比對它 ≡ 比對真客戶端。
 */
public class GoldenVectors {

    static String hex(byte[] b) {
        StringBuilder sb = new StringBuilder();
        for (byte x : b) sb.append(String.format("%02X", x & 0xFF));
        return sb.toString();
    }

    public static void main(String[] args) {
        byte[] iv = new byte[]{(byte) 0x52, (byte) 0x30, (byte) 0x78, (byte) 0x14};
        short version = 113;

        // 1) getNewIv（IV 演化 / funnyShit）
        System.out.println("NEWIV=" + hex(MapleAESOFB.getNewIv(iv)));

        // 2) getPacketHeader（送出頭，用初始 iv + version）
        MapleAESOFB h = new MapleAESOFB(iv.clone(), version);
        System.out.println("HEADER20=" + hex(h.getPacketHeader(20)));
        MapleAESOFB h2 = new MapleAESOFB(iv.clone(), version);
        System.out.println("HEADER1460=" + hex(h2.getPacketHeader(1460)));

        // 3) crypt 小封包（32 bytes = 0x00..0x1F），印加密結果 + 之後的 IV
        byte[] small = new byte[32];
        for (int i = 0; i < small.length; i++) small[i] = (byte) i;
        MapleAESOFB c1 = new MapleAESOFB(iv.clone(), version);
        byte[] enc1 = c1.crypt(small);
        System.out.println("CRYPT32=" + hex(enc1));
        System.out.println("IVAFTER32=" + hex(c1.getIv()));

        // 4) crypt 跨塊邊界（0x5B1 = 1457 bytes），只印前 16 + 後 16 + IV（節省輸出）
        int n = 0x5B1;
        byte[] big = new byte[n];
        for (int i = 0; i < n; i++) big[i] = (byte) ((i * 7 + 3) & 0xFF);
        MapleAESOFB c2 = new MapleAESOFB(iv.clone(), version);
        byte[] enc2 = c2.crypt(big);
        byte[] head = new byte[16], tail = new byte[16];
        System.arraycopy(enc2, 0, head, 0, 16);
        System.arraycopy(enc2, n - 16, tail, 0, 16);
        System.out.println("CRYPT1457_HEAD=" + hex(head));
        System.out.println("CRYPT1457_TAIL=" + hex(tail));
        System.out.println("IVAFTER1457=" + hex(c2.getIv()));

        // 5) send-cipher 版本（0xFFFF-113）的 header，驗證版本互換邏輯
        MapleAESOFB s = new MapleAESOFB(iv.clone(), (short) (0xFFFF - version));
        System.out.println("SENDHEADER20=" + hex(s.getPacketHeader(20)));
    }
}
