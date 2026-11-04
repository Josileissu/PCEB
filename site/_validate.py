import os, re, sys
from html.parser import HTMLParser
from urllib.parse import urlparse

root = os.getcwd()
pages = ["index.html","deb.html","documentacao.html","baixar.html","projeto.html","404.html"]
void = {"area","base","br","col","embed","hr","img","input","link","meta","param","source","track","wbr"}
problems = []

class P(HTMLParser):
    def __init__(self):
        super().__init__(convert_charrefs=True)
        self.stack=[]; self.ids=[]; self.links=[]; self.assets=[]
    def handle_starttag(self, tag, attrs):
        a=dict(attrs)
        if tag not in void:
            self.stack.append((tag, self.getpos()))
        if 'id' in a: self.ids.append(a['id'])
        if tag=='a' and 'href' in a: self.links.append(a['href'])
        if tag in ('link','script','img','source') and ('href' in a or 'src' in a):
            self.assets.append(a.get('href') or a.get('src'))
    def handle_endtag(self, tag):
        if tag in void: return
        if not self.stack:
            problems.append(f"{self.file}:{self.getpos()} fechamento </{tag}> sem abertura")
            return
        t, pos = self.stack.pop()
        if t != tag:
            problems.append(f"{self.file}:{pos} esperava </{t}>, veio </{tag}>")

for name in pages:
    src=open(name,encoding='utf-8').read()
    p=P(); p.file=name; p.feed(src); p.close()
    if p.stack:
        problems.append(f"{name}: tags abertas no fim: {[t for t,_ in p.stack]}")
    dup=[i for i in set(p.ids) if p.ids.count(i)>1]
    if dup: problems.append(f"{name}: IDs duplicados: {dup}")
    for href in p.links + p.assets:
        if not href or href.startswith(('#','http://','https://','mailto:','javascript:','data:')): continue
        path=href.split('#')[0].split('?')[0]
        if path and not os.path.exists(os.path.join(root,path)):
            problems.append(f"{name}: alvo inexistente: {href}")
    for required in ['<title>','name="description"','rel="canonical"']:
        if name!='404.html' and required not in src:
            problems.append(f"{name}: falta {required}")
    if 'armadura.html' in src:
        problems.append(f"{name}: referência antiga a armadura.html")

# XML parse
import xml.etree.ElementTree as ET
try:
    ET.parse('sitemap.xml')
except Exception as e:
    problems.append(f"sitemap.xml inválido: {e}")

# HTML required files
for f in ['sitemap.xml','robots.txt','assets/img/favicon.svg','assets/img/og.png','assets/css/style.css','assets/js/main.js']:
    if not os.path.exists(f): problems.append(f"arquivo ausente: {f}")

# local url set in sitemap
sm=open('sitemap.xml',encoding='utf-8').read()
for name in pages:
    if name=='404.html': continue
    expected='https://pceb.com.br/' + ('' if name=='index.html' else name)
    if expected not in sm: problems.append(f"sitemap sem {expected}")

print('\n'.join(problems) if problems else 'OK: HTML, links, IDs, sitemap e assets validados.')
