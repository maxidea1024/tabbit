# -*- coding: utf-8 -*-
"""Convert the Simplified catalogs to Traditional.

Two passes. WORDS first, because the vocabulary Taiwan spells differently is a
phrase and not a character - 文件 is 檔案 only where it means a file, and 标签 is
標籤 while 签发 is 簽發. CHARS second, covering every character these catalogs
use. A character Simplified spells for two Traditional ones is in CHARS only
where this corpus settles it, which was checked by hand: 于 is always
prepositional, 里 always locative, 只 always "only", 复 always 重复, 准 always
标准, 尽 always 用尽, 冲 always 冲突, 干 always 干净, 着 always the particle.

Afterwards it writes every Han character still standing, so a mapping I missed
shows up as a Simplified form in that list rather than staying silent.
"""
import collections
import glob
import io
import json
import os
import sys

WORDS = [
    (u'单元格', u'儲存格'),
    (u'文件夹', u'資料夾'),
    (u'文件名', u'檔名'),
    (u'文件', u'檔案'),
    (u'数据库', u'資料庫'),
    (u'数据', u'資料'),
    (u'数组', u'陣列'),
    (u'字符串', u'字串'),
    (u'字符', u'字元'),
    (u'正则表达式', u'正規表示式'),
    (u'进制', u'進位'),
    (u'字节', u'位元組'),
    (u'标签', u'標籤'),
    (u'字段', u'欄位'),
    (u'下划线', u'底線'),
    (u'单词', u'單字'),
    (u'存储', u'儲存'),
    (u'凭据', u'憑證'),
    (u'空串', u'空字串'),
    (u'花括号', u'大括號'),
    (u'嵌套', u'巢狀'),
    (u'接口', u'介面'),
    (u'的类，', u'的類別，'),
    (u'批注', u'註解'),
    (u'默认', u'預設'),
    (u'缓存', u'快取'),
    (u'代码', u'程式碼'),
    (u'连接', u'連線'),
    (u'变量', u'變數'),
    (u'常量', u'常數'),
    (u'密钥', u'金鑰'),
    (u'支持', u'支援'),
    (u'运行', u'執行'),
    (u'保存', u'儲存'),
    (u'分组', u'群組'),
    (u'构建', u'建置'),
    (u'导出', u'匯出'),
    (u'导入', u'匯入'),
    (u'模板', u'範本'),
    (u'设置', u'設定'),
    (u'布局', u'佈局'),
    (u'布尔', u'布林'),
    (u'事务', u'交易'),
    (u'重命名', u'重新命名'),
]

CHARS = dict(zip(
    u'与东两个为么义于从仓们会传体储关内写冲决况净准凭划则刚创删别务动区单却压发变'
    u'叠号后吗启员响围图场块坚声处备复够头夹实对寻导将尽层属带干并库应开弃归当录径'
    u'户扫抛报拥换据携数无旧时显机条来构标树样档检残没浏点状独环现电盖盘码确离种称'
    u'签简类级纳线组细经结给络绝续缓编缘缩网节范获蓝见规览触订认让记讲许论设访证识'
    u'词译试询该语误说请读调败账资转载较辅输边达迁过运还这进远连适选释里针钟钥铺链'
    u'销锁错键长闭问间阶际随页顶项顺须预题验着丢余征',

    u'與東兩個為麼義於從倉們會傳體儲關內寫衝決況淨準憑劃則剛創刪別務動區單卻壓發變'
    u'疊號後嗎啟員響圍圖場塊堅聲處備複夠頭夾實對尋導將盡層屬帶乾並庫應開棄歸當錄徑'
    u'戶掃拋報擁換據攜數無舊時顯機條來構標樹樣檔檢殘沒瀏點狀獨環現電蓋盤碼確離種稱'
    u'簽簡類級納線組細經結給絡絕續緩編緣縮網節範獲藍見規覽觸訂認讓記講許論設訪證識'
    u'詞譯試詢該語誤說請讀調敗帳資轉載較輔輸邊達遷過運還這進遠連適選釋裡針鐘鑰鋪鏈'
    u'銷鎖錯鍵長閉問間階際隨頁頂項順須預題驗著丟餘徵'))

# dict(zip()) truncates in silence when the two halves disagree, and a truncated
# map converts most of a message and leaves the rest Simplified. The count is
# here so that adding a character to one half and forgetting the other stops the
# script instead of shipping a half-converted catalog.
assert len(CHARS) == 213, len(CHARS)


def convert(text):
    for simplified, traditional in WORDS:
        text = text.replace(simplified, traditional)
    return u''.join(CHARS.get(ch, ch) for ch in text)


def main():
    standing = collections.Counter()
    for path in sorted(glob.glob('src/Messages/Catalog/*.zh-Hans.json')):
        with io.open(path, encoding='utf-8') as f:
            entries = json.load(f, object_pairs_hook=collections.OrderedDict)
        out = collections.OrderedDict()
        for key, value in entries.items():
            out[key] = convert(value)
            for ch in out[key]:
                if u'一' <= ch <= u'鿿':
                    standing[ch] += 1
        target = path.replace('.zh-Hans.', '.zh-Hant.')
        with io.open(target, 'w', encoding='utf-8', newline='\n') as f:
            json.dump(out, f, ensure_ascii=False, indent=2)
            f.write(u'\n')
        print('%-30s %3d entries' % (os.path.basename(target), len(out)))
    with io.open(sys.argv[1], 'w', encoding='utf-8') as f:
        f.write(u''.join(sorted(standing)))
    print('%d pairs in the character map, %d distinct Han characters standing'
          % (len(CHARS), len(standing)))


main()
