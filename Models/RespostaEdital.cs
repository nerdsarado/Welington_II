namespace Welington_II.Models
{
    public class RespostaEdital
    {
        public string telefone { get; set; }
        public string id_edital { get; set; }
        public bool aprovado { get; set; }
    }

    public class DadosEnvioEdital
    {
        public string id_edital { get; set; }
        public string orgao { get; set; }
        public string local { get; set; }
        public string valor { get; set; }
        public string objeto { get; set; }
    }
}