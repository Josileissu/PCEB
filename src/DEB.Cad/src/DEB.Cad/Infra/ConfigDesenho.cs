using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Configuracao;
using DEB.Core.Persistencia;

namespace DEB.Cad.Infra;

/// <summary>
/// Configuração que o desenho "leva" consigo (seção 5.6a): Xrecord ARMADURA_BR_CONFIG no NOD com o
/// JSON fatiado. Sem ela, vale a configuração do usuário. Os valores correntes (bitola, quantidade…)
/// são sempre os do usuário.
/// </summary>
internal static class ConfigDesenho
{
    public const string Chave = "ARMADURA_BR_CONFIG";

    // null = o desenho não tem configuração própria (usa a do usuário)
    private static readonly Dictionary<Database, ConfiguracaoArmadura> Cache = new Dictionary<Database, ConfiguracaoArmadura>();

    // ---- unidades do desenho (2026-11-10): registro pequeno e separado, sobreposto à configuração (do desenho ou do usuário) ----
    public const string ChaveUnidades = "DEB_UNIDADES";
    // null = o desenho não escolheu unidades (valem as da configuração)
    private static readonly Dictionary<Database, (UnidadeDesenho Desenho, UnidadeApresentacao Apresentacao)?> Unidades =
        new Dictionary<Database, (UnidadeDesenho, UnidadeApresentacao)?>();
    // configuração com as unidades do desenho aplicadas: refeita quando a base ou a configuração do usuário mudam
    private static readonly Dictionary<Database, (int Versao, ConfiguracaoArmadura Base, ConfiguracaoArmadura Cfg)> ComUnidades =
        new Dictionary<Database, (int, ConfiguracaoArmadura, ConfiguracaoArmadura)>();
    /// <summary>Muda quando a configuração do usuário é salva ou relida (as cópias com unidades são refeitas).</summary>
    public static int Versao;

    /// <summary>Configuração efetiva do desenho.</summary>
    public static ConfiguracaoArmadura Obter(Database db)
    {
        if (db == null) return Plugin.Config;
        if (!Cache.TryGetValue(db, out var cfg))
        {
            cfg = Ler(db);
            Cache[db] = cfg;
        }
        if (!Unidades.ContainsKey(db)) Unidades[db] = LerUnidades(db);
        return AplicarUnidades(db, cfg ?? Plugin.Config);
    }

    /// <summary>Só o que já está em cache (para o Overrule, que não deve abrir transações durante o desenho).</summary>
    public static ConfiguracaoArmadura ObterSemLer(Database db) =>
        db == null ? Plugin.Config : AplicarUnidades(db, Cache.TryGetValue(db, out var cfg) && cfg != null ? cfg : Plugin.Config);

    private static ConfiguracaoArmadura AplicarUnidades(Database db, ConfiguracaoArmadura baseCfg)
    {
        if (!Unidades.TryGetValue(db, out var u) || u == null) return baseCfg;
        if (baseCfg.UnidadeDesenho == u.Value.Desenho && baseCfg.UnidadeApresentacao == u.Value.Apresentacao) return baseCfg;
        if (ComUnidades.TryGetValue(db, out var c) && c.Versao == Versao && ReferenceEquals(c.Base, baseCfg)) return c.Cfg;
        var copia = baseCfg.Clonar();
        copia.UnidadeDesenho = u.Value.Desenho;
        copia.UnidadeApresentacao = u.Value.Apresentacao;
        ComUnidades[db] = (Versao, baseCfg, copia);
        return copia;
    }

    /// <summary>As unidades gravadas no desenho; null = ele não tem.</summary>
    public static (UnidadeDesenho Desenho, UnidadeApresentacao Apresentacao)? UnidadesDoDesenho(Database db)
    {
        if (db == null) return null;
        if (!Unidades.ContainsKey(db)) Unidades[db] = LerUnidades(db);
        return Unidades[db];
    }

    private static (UnidadeDesenho, UnidadeApresentacao)? LerUnidades(Database db)
    {
        try
        {
            (UnidadeDesenho, UnidadeApresentacao)? r = null;
            using (var tr = db.TransacaoLeve())
            {
                var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
                if (nod.Contains(ChaveUnidades))
                {
                    var xr = (Xrecord)tr.GetObject(nod.GetAt(ChaveUnidades), OpenMode.ForRead);
                    using var rb = xr.Data;
                    var v = rb?.AsArray().Where(tv => tv.TypeCode == (short)DxfCode.Int16).Select(tv => Convert.ToInt32(tv.Value)).ToList();
                    if (v != null && v.Count >= 2) r = ((UnidadeDesenho)v[0], (UnidadeApresentacao)v[1]);
                }
                tr.Commit();
            }
            return r;
        }
        catch (Exception ex) { Log.Erro("ConfigDesenho.LerUnidades", ex); return null; }
    }

    /// <summary>Grava as unidades no desenho (ARMUNIDADES).</summary>
    public static void GravarUnidades(Database db, Transaction tr, UnidadeDesenho desenho, UnidadeApresentacao apresentacao)
    {
        var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);
        using var rb = new ResultBuffer(new TypedValue((short)DxfCode.Int16, (short)desenho), new TypedValue((short)DxfCode.Int16, (short)apresentacao));
        if (nod.Contains(ChaveUnidades)) ((Xrecord)tr.GetObject(nod.GetAt(ChaveUnidades), OpenMode.ForWrite)).Data = rb;
        else
        {
            var xr = new Xrecord { Data = rb };
            nod.SetAt(ChaveUnidades, xr);
            tr.AddNewlyCreatedDBObject(xr, true);
        }
        Unidades[db] = (desenho, apresentacao);
        ComUnidades.Remove(db);
    }

    /// <summary>O desenho tem configuração PRÓPRIA (a completa, gravada no DWG) — as unidades sozinhas não contam.</summary>
    public static bool TemNoDesenho(Database db)
    {
        Obter(db);
        return db != null && Cache.TryGetValue(db, out var c) && c != null;
    }

    public static void Esquecer(Database db) { if (db != null) { Cache.Remove(db); Unidades.Remove(db); ComUnidades.Remove(db); } }

    public static void EsquecerTodos() { Cache.Clear(); Unidades.Clear(); ComUnidades.Clear(); }

    /// <summary>Copia os valores correntes do usuário para as configurações de desenho em memória.</summary>
    public static void SincronizarCorrentes()
    {
        foreach (var c in Cache.Values.Where(c => c != null)) CopiarCorrentes(Plugin.Config, c);
        foreach (var c in ComUnidades.Values) CopiarCorrentes(Plugin.Config, c.Cfg);
    }

    private static void CopiarCorrentes(ConfiguracaoArmadura de, ConfiguracaoArmadura para)
    {
        para.BitolaCorrente = de.BitolaCorrente;
        para.AcoCorrente = de.AcoCorrente;
        para.QuantidadeCorrente = de.QuantidadeCorrente;
        para.EspacamentoCorrente = de.EspacamentoCorrente;
        para.MostrarPaleta = de.MostrarPaleta;
        para.MostrarComandos = de.MostrarComandos;
        para.EscalaFaixa = de.EscalaFaixa;
    }

    /// <summary>
    /// Muda um valor "do desenho" (ex.: tipo de numeração): na configuração gravada no DWG, se houver;
    /// senão na do usuário (salva no config.json).
    /// </summary>
    public static void Alterar(Database db, Transaction tr, Action<ConfiguracaoArmadura> mudar)
    {
        if (TemNoDesenho(db))
        {
            var cfg = Obter(db).Clonar();
            mudar(cfg);
            Gravar(db, tr, cfg);
        }
        else
        {
            mudar(Plugin.Config);
            Plugin.SalvarConfig();
        }
    }

    private static ConfiguracaoArmadura Ler(Database db)
    {
        try
        {
            string json = null;
            // sempre Commit: no GstarCAD, abortar transações fora de hora atrapalha as notificações
            using (var tr = db.TransacaoLeve())
            {
                var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
                if (nod.Contains(Chave))
                {
                    var xr = (Xrecord)tr.GetObject(nod.GetAt(Chave), OpenMode.ForRead);
                    using var rb = xr.Data;
                    if (rb != null)
                        json = string.Concat(rb.AsArray().Where(tv => tv.TypeCode == (short)DxfCode.XTextString).Select(tv => (string)tv.Value));
                }
                tr.Commit();
            }
            if (string.IsNullOrEmpty(json)) return null;
            var cfg = ConfiguracaoArmadura.DeJson(json);
            CopiarCorrentes(Plugin.Config, cfg);
            return cfg;
        }
        catch (Exception ex)
        {
            Log.Erro("ConfigDesenho.Ler", ex);
            return null;
        }
    }

    /// <summary>Grava (ou substitui) a configuração no desenho.</summary>
    public static void Gravar(Database db, Transaction tr, ConfiguracaoArmadura cfg)
    {
        var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);
        var tvs = CodecDados.Fatiar(cfg.ParaJson()).Select(p => new TypedValue((short)DxfCode.XTextString, p)).ToArray();
        using var rb = new ResultBuffer(tvs);
        if (nod.Contains(Chave))
        {
            var xr = (Xrecord)tr.GetObject(nod.GetAt(Chave), OpenMode.ForWrite);
            xr.Data = rb;
        }
        else
        {
            var xr = new Xrecord { Data = rb };
            nod.SetAt(Chave, xr);
            tr.AddNewlyCreatedDBObject(xr, true);
        }
        var copia = cfg.Clonar();
        CopiarCorrentes(Plugin.Config, copia);
        Cache[db] = copia;
    }

    public static bool Remover(Database db, Transaction tr)
    {
        var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
        Cache[db] = null;
        if (!nod.Contains(Chave)) return false;
        tr.GetObject(nod.GetAt(Chave), OpenMode.ForWrite).Erase();
        return true;
    }

    /// <summary>Lê a configuração de um DWG fechado (para o autoteste e, no futuro, a RA por pasta).</summary>
    public static ConfiguracaoArmadura LerDeArquivo(string caminho)
    {
        using var db = new Database(false, true);
        db.ReadDwgFile(caminho, FileOpenMode.OpenForReadAndAllShare, true, "");
        return Ler(db);
    }
}
