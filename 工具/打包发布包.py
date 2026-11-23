# -*- coding: utf-8 -*-
r"""
打包 SiNiSistar2 全异常状态 Mod 发布包。

用法：
    python 工具\打包发布包.py               # 打包到 E:\TAML\发布包\
    python 工具\打包发布包.py -o D:\out     # 指定输出目录
    python 工具\打包发布包.py --dry-run     # 只检查清单，不写文件

白名单式打包：只收下面列出的顶层文件与目录，仓库里新增的说明文档不会混进包。
版本号从 源码\AllAbnormalMod.csproj 的 Version 读取。
"""

import argparse
import hashlib
import os
import re
import sys
import zipfile

# 仓库根 = 本脚本所在目录的上一级
仓库根 = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

# 打进包的顶层文件（顺序即压缩包内顺序）
顶层文件 = [
    'README.md',
    'LICENSE',
    'install.bat',
    'install.ps1',
    '更新公告.txt',
    '预览图_打码.png',
]

# 打进包的顶层目录
顶层目录 = ['ModData', '工具']


def 读版本号():
    p = os.path.join(仓库根, '源码', 'AllAbnormalMod.csproj')
    if not os.path.exists(p):
        sys.exit('找不到 %s' % p)
    t = open(p, encoding='utf-8').read()
    m = re.search(r'<Version>\s*([0-9.]+)\s*</Version>', t)
    if not m:
        sys.exit('csproj 里没找到 <Version>')
    return m.group(1)


def 收集清单():
    项 = []
    for f in 顶层文件:
        p = os.path.join(仓库根, f)
        if not os.path.exists(p):
            sys.exit('缺少顶层文件：%s' % f)
        项.append((p, f))
    for d in 顶层目录:
        base = os.path.join(仓库根, d)
        if not os.path.isdir(base):
            sys.exit('缺少顶层目录：%s' % d)
        for 当前, 子目录们, 文件名们 in os.walk(base):
            子目录们[:] = sorted(d for d in 子目录们 if d != '__pycache__')
            for fn in sorted(文件名们):
                full = os.path.join(当前, fn)
                rel = os.path.relpath(full, 仓库根).replace('\\', '/')
                项.append((full, rel))
    return 项


def 主流程():
    ap = argparse.ArgumentParser()
    ap.add_argument('-o', '--out', default=r'E:\TAML\发布包')
    ap.add_argument('--dry-run', action='store_true')
    args = ap.parse_args()

    版本 = 读版本号()
    项 = 收集清单()

    print('版本      : %s' % 版本)
    print('清单项数  : %d' % len(项))
    print('顶层结构  :')
    for d in 顶层目录:
        n = len([1 for _, rel in 项 if rel.startswith(d + '/')])
        print('    %-10s %d 个文件' % (d + '/', n))

    if args.dry_run:
        print('\n--dry-run，不写文件。')
        return

    os.makedirs(args.out, exist_ok=True)
    包名 = 'SiNiSistar2-AllAbnormalMod-v%s.zip' % 版本
    包路径 = os.path.join(args.out, 包名)
    if os.path.exists(包路径):
        os.remove(包路径)

    原始总量 = 0
    with zipfile.ZipFile(包路径, 'w', zipfile.ZIP_DEFLATED, compresslevel=6) as z:
        for full, rel in 项:
            z.write(full, rel)
            原始总量 += os.path.getsize(full)

    # ---- 校验 ----
    z = zipfile.ZipFile(包路径)
    names = [n for n in z.namelist() if not n.endswith('/')]
    坏 = z.testzip()
    不符 = []
    for 全路径, rel in 项:
        if rel not in names:
            不符.append(rel)
        elif z.getinfo(rel).file_size != os.path.getsize(全路径):
            不符.append(rel)
    z.close()

    大小 = os.path.getsize(包路径)
    摘要 = hashlib.sha256(open(包路径, 'rb').read()).hexdigest().upper()

    print('\n原始总量  : %d 字节 (%.1f MB)' % (原始总量, 原始总量 / 1048576))
    print('压缩后    : %d 字节 (%.1f MB)' % (大小, 大小 / 1048576))
    print('条目数    : %d' % len(names))
    print('CRC       : %s' % ('全部通过' if 坏 is None else '!! 出错：' + str(坏)))
    print('大小核对  : %s' % ('全部一致' if not 不符 else '!! %d 项不符' % len(不符)))
    print('SHA256    : %s' % 摘要)
    print('输出      : %s' % 包路径)


if __name__ == '__main__':
    主流程()
