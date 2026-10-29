"""Consulta o índice local das normas (ver indexar.py).

  python -E -P buscar.py "consolo tirante ancoragem"          → páginas mais relevantes, com trecho
  python -E -P buscar.py "consolo" --doc 9062                  → só documentos cujo nome contém "9062"
  python -E -P buscar.py --pagina "9062" 45                    → texto completo da página 45
  python -E -P buscar.py --docs [filtro]                       → lista os documentos indexados

Sintaxe FTS5: palavras soltas = todas; "frase exata"; termo* = prefixo; OR; NEAR(a b, 10).
"""
import os
import sqlite3
import sys

AQUI = os.path.dirname(os.path.abspath(__file__))
BANCO = os.path.normpath(os.path.join(AQUI, "..", "..", "info_auxiliar", "indice_normas", "normas.sqlite"))


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    con = sqlite3.connect(BANCO)
    a = sys.argv[1:]
    if not a:
        print(__doc__)
        return
    if a[0] == "--docs":
        filtro = f"%{a[1]}%" if len(a) > 1 else "%"
        for c, p, ch in con.execute("SELECT caminho, npaginas, caracteres FROM docs WHERE caminho LIKE ? ORDER BY caminho", (filtro,)):
            print(f"{p or '?':>5} pág  {c}" + ("" if (ch or 0) > 100 else "  [sem texto]"))
        return
    if a[0] == "--pagina":
        doc = con.execute("SELECT id, caminho FROM docs WHERE caminho LIKE ? ORDER BY length(caminho)", (f"%{a[1]}%",)).fetchone()
        if not doc:
            print("documento não encontrado")
            return
        t = con.execute("SELECT texto FROM paginas WHERE doc_id=? AND pagina=?", (doc[0], int(a[2]))).fetchone()
        print(f"== {doc[1]} — página {a[2]}\n{t[0] if t else '(sem página)'}")
        return
    consulta = a[0]
    filtro_doc = "%"
    limite = 15
    if "--doc" in a:
        filtro_doc = f"%{a[a.index('--doc') + 1]}%"
    if "--n" in a:
        limite = int(a[a.index("--n") + 1])
    sql = """SELECT d.caminho, paginas.pagina, snippet(paginas, 2, '[', ']', ' … ', 24)
             FROM paginas JOIN docs d ON d.id = paginas.doc_id
             WHERE paginas MATCH ? AND d.caminho LIKE ? ORDER BY rank LIMIT ?"""
    for caminho, pagina, trecho in con.execute(sql, (consulta, filtro_doc, limite)):
        print(f"{caminho} — p. {pagina}\n    {' '.join(trecho.split())}\n")


if __name__ == "__main__":
    main()
