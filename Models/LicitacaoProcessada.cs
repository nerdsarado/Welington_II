namespace Welington_II.Models
{
    public class LicitacaoProcessada
    {
        public string Id { get; set; }
        public string Titulo { get; set; }
        public string Estado { get; set; }
        public string PalavraChave { get; set; }
        public DateTime DataProcessamento { get; set; }
        public string Url { get; set; }
        public string CaminhoArquivo { get; set; }
        public bool Aprovado { get; set; }
        public string Orgao { get; set; }
        public string Local { get; set; }
        public string Valor { get; set; }
        public string Objeto { get; set; }
        public string Telefone { get; set; }
    }
}