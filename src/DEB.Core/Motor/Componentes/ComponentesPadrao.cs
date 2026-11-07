namespace DEB.Core.Motor.Componentes;

/// <summary>Componentes que vêm com o DEB (as receitas os chamam pelo <see cref="IComponente.Tipo"/>).</summary>
public static class ComponentesPadrao
{
    public static readonly IComponente ApoiosEstacas = new ApoiosEstacas();
    public static readonly IComponente CorpoPrismatico = new CorpoPrismatico();
    public static readonly IComponente CorposCirculares = new CorposCirculares();
    public static readonly IComponente Niveis = new Niveis();
    public static readonly IComponente BitolaEfetiva = new BitolaEfetiva();
    public static readonly IComponente Aviso = new Aviso();
    public static readonly IComponente FaixaSobreApoios = new FaixaSobreApoios();
    public static readonly IComponente MalhaFace = new MalhaFace();
    public static readonly IComponente ContornoLateral = new ContornoLateral();
    public static readonly IComponente EstribosVerticais = new EstribosVerticais();
    public static readonly IComponente CaliceForma = new CaliceForma();
    public static readonly IComponente CaliceArmaduras = new CaliceArmaduras();
    public static readonly IComponente Arranque = new Arranque();
    public static readonly IComponente SecaoLongitudinal = new SecaoLongitudinal();
    public static readonly IComponente VerificacoesGeometria = new VerificacoesGeometria();
    public static readonly IComponente VerificacoesCalice = new VerificacoesCalice();

    /// <summary>Catálogo novo com todos os componentes padrão.</summary>
    public static CatalogoComponentes Catalogo() => new CatalogoComponentes()
        .Registrar(ApoiosEstacas).Registrar(CorpoPrismatico).Registrar(CorposCirculares).Registrar(Niveis)
        .Registrar(BitolaEfetiva).Registrar(Aviso).Registrar(FaixaSobreApoios).Registrar(MalhaFace)
        .Registrar(ContornoLateral).Registrar(EstribosVerticais).Registrar(CaliceForma).Registrar(CaliceArmaduras)
        .Registrar(Arranque).Registrar(SecaoLongitudinal).Registrar(VerificacoesGeometria).Registrar(VerificacoesCalice);
}
