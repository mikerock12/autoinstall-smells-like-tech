using System;
using System.Text.RegularExpressions;

namespace AutoInstall
{
    // Plano de energia: durante o processo, duplica o "Desempenho Máximo"
    // (Ultimate Performance) e zera todos os timeouts (tela, discos, suspensao,
    // hibernacao = nunca). No final, volta para o Equilibrado (recomendado)
    // e apaga o plano temporario.
    public static class Energia
    {
        const string GUID_ULTIMATE = "e9a42b02-d5df-448d-aa00-03f14749eb61";      // Desempenho Maximo
        const string GUID_ALTO = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";          // Alto Desempenho
        const string GUID_EQUILIBRADO = "381b4222-f694-41f0-9685-ff5bb260df2e";   // Equilibrado (recomendado)

        // Tudo em 0 = nunca desligar, na tomada e na bateria.
        static readonly string[] AJUSTES_SEM_DESLIGAR = {
            "/change monitor-timeout-ac 0",   "/change monitor-timeout-dc 0",
            "/change disk-timeout-ac 0",      "/change disk-timeout-dc 0",
            "/change standby-timeout-ac 0",   "/change standby-timeout-dc 0",
            "/change hibernate-timeout-ac 0", "/change hibernate-timeout-dc 0"
        };

        static readonly Regex RxGuid = new Regex(
            "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");

        public static void AtivarMaximo(Estado estado, Action<string> log)
        {
            var atual = Executor.Rodar("powercfg.exe", "/getactivescheme");
            Match m = RxGuid.Match(atual.Saida);
            if (m.Success) estado.PlanoOriginal = m.Value;

            // Duplica o Desempenho Maximo; se a edicao do Windows nao o expuser,
            // cai para o Alto Desempenho.
            var dup = Executor.Rodar("powercfg.exe", "/duplicatescheme " + GUID_ULTIMATE);
            m = RxGuid.Match(dup.Saida);
            if (!m.Success)
            {
                dup = Executor.Rodar("powercfg.exe", "/duplicatescheme " + GUID_ALTO);
                m = RxGuid.Match(dup.Saida);
            }

            if (m.Success)
            {
                estado.PlanoUltra = m.Value;
                Executor.Rodar("powercfg.exe", "/changename " + m.Value +
                    " \"Smells Like Tech - Desempenho Total (temporário)\"" +
                    " \"Plano temporário usado durante o pós-formatação. Removido automaticamente ao final.\"");
                Executor.Rodar("powercfg.exe", "/setactive " + m.Value);
                log("Plano de energia de desempenho máximo ativado (temporário).");
            }
            else
            {
                log("Aviso: não consegui duplicar um plano de alto desempenho; ajustando o plano atual.");
            }

            foreach (string a in AJUSTES_SEM_DESLIGAR) Executor.Rodar("powercfg.exe", a);
            log("Tela, discos, suspensão e hibernação configurados para NUNCA desligar (temporário).");
        }

        // Modo "Sou ousado": o plano de desempenho maximo FICA na maquina.
        // O plano temporario perde o "(temporario)" do nome, os timeouts sao
        // reforcados (o Windows Update mexe neles ao longo do processo) e nada
        // e apagado.
        public static void ManterMaximo(Estado estado, Action<string> log)
        {
            if (!string.IsNullOrEmpty(estado.PlanoUltra))
            {
                Executor.Rodar("powercfg.exe", "/changename " + estado.PlanoUltra +
                    " \"Smells Like Tech - Desempenho Total\"" +
                    " \"Plano de desempenho máximo aplicado pelo AutoInstall: tela, discos," +
                    " suspensão e hibernação nunca desligam. Trocar em Opções de Energia.\"");
                // Os /change abaixo valem para o plano ATIVO: garante que e ele.
                Executor.Rodar("powercfg.exe", "/setactive " + estado.PlanoUltra);
            }

            foreach (string a in AJUSTES_SEM_DESLIGAR) Executor.Rodar("powercfg.exe", a);
            log("Plano de energia de desempenho máximo mantido: nada desliga, suspende ou hiberna.");
        }

        public static void Restaurar(Estado estado, Action<string> log)
        {
            var r = Executor.Rodar("powercfg.exe", "/setactive " + GUID_EQUILIBRADO);
            if (!r.Ok && !string.IsNullOrEmpty(estado.PlanoOriginal))
                Executor.Rodar("powercfg.exe", "/setactive " + estado.PlanoOriginal);

            if (!string.IsNullOrEmpty(estado.PlanoUltra))
                Executor.Rodar("powercfg.exe", "/delete " + estado.PlanoUltra);

            log("Plano de energia Equilibrado (recomendado) restaurado; plano temporário removido.");
        }
    }
}
