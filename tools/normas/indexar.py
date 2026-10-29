"""Índice local das normas do usuário (S:\\Meu Drive\\Normas) para consulta rápida.

Extrai o texto de cada página dos PDFs (pymupdf) para um SQLite com busca de texto (FTS5, sem acentos).
Incremental: só relê os arquivos novos ou alterados (tamanho/data). PDFs escaneados (sem texto) ficam
marcados no catálogo. O índice fica fora do Git: armadura/info_auxiliar/indice_normas/.

Uso: python -E -P indexar.py [pasta_das_normas]
"""
import os
import sqlite3
import sys
import time

import fitz  # pymupdf

RAIZ = sys.argv[1] if len(sys.argv) > 1 else r"S:\Meu Drive\Normas"
AQUI = os.path.dirname(os.path.abspath(__file__))
DESTINO = os.path.normpath(os.path.join(AQUI, "..", "..", "info_auxiliar", "indice_normas"))
os.makedirs(DESTINO, exist_ok=True)
BANCO = os.path.join(DESTINO, "normas.sqlite")


def abrir():
    con = sqlite3.connect(BANCO)
    con.execute("""CREATE TABLE IF NOT EXISTS docs(
        id INTEGER PRIMARY KEY, caminho TEXT UNIQUE, pasta TEXT, nome TEXT,
        tamanho INTEGER, mtime REAL, npaginas INTEGER, caracteres INTEGER, erro TEXT)""")
    con.execute("""CREATE VIRTUAL TABLE IF NOT EXISTS paginas USING fts5(
        doc_id UNINDEXED, pagina UNINDEXED, texto, tokenize='unicode61 remove_diacritics 2')""")
    return con


def indexar(con, caminho):
    st = os.stat(caminho)
    rel = os.path.relpath(caminho, RAIZ)
    row = con.execute("SELECT id, tamanho, mtime FROM docs WHERE caminho=?", (rel,)).fetchone()
    if row and row[1] == st.st_size and abs(row[2] - st.st_mtime) < 1:
        return "igual"
    if row:
        con.execute("DELETE FROM paginas WHERE doc_id=?", (row[0],))
        con.execute("DELETE FROM docs WHERE id=?", (row[0],))
    cur = con.execute("INSERT INTO docs(caminho, pasta, nome, tamanho, mtime) VALUES(?,?,?,?,?)",
                      (rel, os.path.dirname(rel), os.path.basename(rel), st.st_size, st.st_mtime))
    doc_id = cur.lastrowid
    try:
        with fitz.open(caminho) as d:
            total = 0
            for i, pg in enumerate(d):
                t = pg.get_text()
                total += len(t.strip())
                con.execute("INSERT INTO paginas(doc_id, pagina, texto) VALUES(?,?,?)", (doc_id, i + 1, t))
            con.execute("UPDATE docs SET npaginas=?, caracteres=? WHERE id=?", (d.page_count, total, doc_id))
        return "novo"
    except Exception as ex:  # PDF protegido/corrompido: fica no catálogo com o erro
        con.execute("UPDATE docs SET erro=? WHERE id=?", (str(ex)[:300], doc_id))
        return "erro"


def catalogo(con):
    linhas = ["# Catálogo do índice de normas", "",
              f"Origem: `{RAIZ}` — gerado em {time.strftime('%Y-%m-%d %H:%M')}. Busca: `python -E -P tools/normas/buscar.py \"termos\"`.", "",
              "| Pasta | Arquivo | Pág. | Texto |", "|---|---|---|---|"]
    for pasta, nome, pags, chars, erro in con.execute(
            "SELECT pasta, nome, npaginas, caracteres, erro FROM docs ORDER BY pasta, nome"):
        if erro:
            estado = "erro: " + erro[:60]
        elif not pags or (chars or 0) < 200 * (pags or 1) * 0.1:
            estado = "**escaneado (sem texto)**"
        else:
            estado = "ok"
        linhas.append(f"| {pasta} | {nome} | {pags or ''} | {estado} |")
    with open(os.path.join(DESTINO, "CATALOGO.md"), "w", encoding="utf-8") as f:
        f.write("\n".join(linhas) + "\n")


def main():
    con = abrir()
    vistos = set()
    cont = {"novo": 0, "igual": 0, "erro": 0}
    for pasta, _, arquivos in os.walk(RAIZ):
        for a in sorted(arquivos):
            if not a.lower().endswith(".pdf"):
                continue
            c = os.path.join(pasta, a)
            vistos.add(os.path.relpath(c, RAIZ))
            r = indexar(con, c)
            cont[r] += 1
            con.commit()
            print(f"[{r}] {os.path.relpath(c, RAIZ)}", flush=True)
    # arquivos removidos da pasta saem do índice
    for doc_id, rel in con.execute("SELECT id, caminho FROM docs").fetchall():
        if rel not in vistos:
            con.execute("DELETE FROM paginas WHERE doc_id=?", (doc_id,))
            con.execute("DELETE FROM docs WHERE id=?", (doc_id,))
    con.commit()
    catalogo(con)
    print(f"fim: {cont}; banco {BANCO}")


if __name__ == "__main__":
    main()
