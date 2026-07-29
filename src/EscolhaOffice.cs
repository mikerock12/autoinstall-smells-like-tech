using System;
using System.Drawing;
using System.Windows.Forms;

namespace AutoInstall
{
    // Pergunta UMA unica vez, no comeco de tudo, qual Office instalar. A
    // escolha importa: a versao empresarial nao ativa com assinatura pessoal
    // do cliente e vice-versa. Fica guardada no estado e reaproveitada nas
    // retomadas depois de cada reinicializacao, para o processo seguir sozinho.
    // Sem resposta em 30 segundos, segue no padrao (Personal/Família).
    public class EscolhaOffice : Form
    {
        public string Edicao = InstaladorOffice.EDICAO_CONSUMIDOR;

        readonly Label lblContagem;
        readonly Timer timer;
        int restantes = 30;

        public EscolhaOffice()
        {
            Text = "AutoInstall · Qual Office instalar?";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(560, 300);
            BackColor = Tema.Fundo;
            TopMost = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { }

            var titulo = new Label();
            titulo.Text = "Qual edição do Office instalar nesta máquina?";
            titulo.Font = new Font("Segoe UI Semibold", 13f);
            titulo.ForeColor = Tema.Laranja;
            titulo.TextAlign = ContentAlignment.MiddleCenter;
            titulo.SetBounds(0, 18, 560, 32);
            Controls.Add(titulo);

            var ajuda = new Label();
            ajuda.Text = "A edição precisa combinar com a assinatura do cliente, senão o Office\n" +
                         "instala mas não ativa. Em português (pt-BR), sempre.";
            ajuda.Font = new Font("Segoe UI", 9.5f);
            ajuda.ForeColor = Tema.TextoSuave;
            ajuda.TextAlign = ContentAlignment.MiddleCenter;
            ajuda.SetBounds(0, 52, 560, 40);
            Controls.Add(ajuda);

            Adicionar("Microsoft 365 Personal / Família   (padrão)",
                "Para o cliente que assina o Microsoft 365 pessoal ou familiar.",
                InstaladorOffice.EDICAO_CONSUMIDOR, 100);
            Adicionar("Microsoft 365 Apps for enterprise",
                "Para cliente com assinatura corporativa (conta da empresa).",
                InstaladorOffice.EDICAO_EMPRESARIAL, 156);
            Adicionar("Não instalar o Office",
                "A máquina já tem Office licenciado ou o cliente não quer.",
                InstaladorOffice.EDICAO_NENHUMA, 212);

            lblContagem = new Label();
            lblContagem.Font = new Font("Segoe UI", 9f);
            lblContagem.ForeColor = Tema.TextoSuave;
            lblContagem.TextAlign = ContentAlignment.MiddleCenter;
            lblContagem.SetBounds(0, 268, 560, 22);
            Controls.Add(lblContagem);

            AtualizarContagem();
            timer = new Timer();
            timer.Interval = 1000;
            timer.Tick += delegate
            {
                restantes--;
                if (restantes <= 0) { Escolher(InstaladorOffice.EDICAO_CONSUMIDOR); return; }
                AtualizarContagem();
            };
            timer.Start();
        }

        void Adicionar(string texto, string ajuda, string edicao, int y)
        {
            var botao = new Button();
            botao.Text = texto;
            botao.Font = new Font("Segoe UI Semibold", 10.5f);
            botao.FlatStyle = FlatStyle.Flat;
            botao.FlatAppearance.BorderColor = Tema.Borda;
            botao.BackColor = Color.FromArgb(28, 31, 38);
            botao.ForeColor = Tema.Texto;
            botao.TextAlign = ContentAlignment.MiddleLeft;
            botao.Padding = new Padding(14, 0, 0, 0);
            botao.SetBounds(40, y, 480, 30);
            botao.Cursor = Cursors.Hand;
            botao.Click += delegate { Escolher(edicao); };
            Controls.Add(botao);

            var lbl = new Label();
            lbl.Text = ajuda;
            lbl.Font = new Font("Segoe UI", 8.5f);
            lbl.ForeColor = Tema.TextoSuave;
            lbl.SetBounds(54, y + 31, 480, 18);
            Controls.Add(lbl);
        }

        void AtualizarContagem()
        {
            lblContagem.Text = string.Format(
                "Sem resposta, sigo no padrão (Personal/Família) em {0} segundo{1}...",
                restantes, restantes == 1 ? "" : "s");
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            timer.Stop();   // fechar no X mantem o padrao ja definido
            base.OnFormClosing(e);
        }

        void Escolher(string edicao)
        {
            timer.Stop();
            Edicao = edicao;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
