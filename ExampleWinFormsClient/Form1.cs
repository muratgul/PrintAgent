using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.AspNetCore.SignalR.Client;

namespace PrintAgentWinForms
{
    public partial class Form1 : Form
    {
        private HubConnection _connection;
        private string _correlationId = Guid.NewGuid().ToString();

        // UI Controls
        private TextBox txtHubUrl;
        private Button btnConnect;
        private ComboBox cmbAgents;
        private Button btnRefreshAgents;
        private ComboBox cmbPrinters;
        private Button btnRefreshPrinters;
        private TextBox txtFilePath;
        private Button btnSelectFile;
        private Button btnPrint;
        private TextBox txtLog;

        public Form1()
        {
            InitializeUI();
        }

        private void InitializeUI()
        {
            this.Text = "Print Agent Client";
            this.Size = new Size(600, 500);

            Label lblUrl = new Label { Text = "Hub URL:", Location = new Point(10, 15), AutoSize = true };
            txtHubUrl = new TextBox { Location = new Point(100, 12), Width = 350, Text = "http://localhost:5000/printhub" };
            btnConnect = new Button { Text = "Connect", Location = new Point(460, 10), Width = 100 };
            btnConnect.Click += BtnConnect_Click;

            Label lblAgent = new Label { Text = "Agent:", Location = new Point(10, 45), AutoSize = true };
            cmbAgents = new ComboBox { Location = new Point(100, 42), Width = 250, DropDownStyle = ComboBoxStyle.DropDownList };
            btnRefreshAgents = new Button { Text = "Get Agents", Location = new Point(360, 40), Width = 100 };
            btnRefreshAgents.Click += BtnRefreshAgents_Click;
            btnRefreshAgents.Enabled = false;

            Label lblPrinter = new Label { Text = "Printer:", Location = new Point(10, 75), AutoSize = true };
            cmbPrinters = new ComboBox { Location = new Point(100, 72), Width = 250, DropDownStyle = ComboBoxStyle.DropDownList };
            btnRefreshPrinters = new Button { Text = "Get Printers", Location = new Point(360, 70), Width = 100 };
            btnRefreshPrinters.Click += BtnRefreshPrinters_Click;
            btnRefreshPrinters.Enabled = false;

            Label lblFile = new Label { Text = "File:", Location = new Point(10, 105), AutoSize = true };
            txtFilePath = new TextBox { Location = new Point(100, 102), Width = 350, ReadOnly = true };
            btnSelectFile = new Button { Text = "Browse...", Location = new Point(460, 100), Width = 100 };
            btnSelectFile.Click += BtnSelectFile_Click;

            btnPrint = new Button { Text = "Send Print Job", Location = new Point(100, 132), Width = 150 };
            btnPrint.Click += BtnPrint_Click;
            btnPrint.Enabled = false;

            txtLog = new TextBox { Location = new Point(10, 170), Width = 550, Height = 270, Multiline = true, ScrollBars = ScrollBars.Vertical, ReadOnly = true };

            this.Controls.Add(lblUrl);
            this.Controls.Add(txtHubUrl);
            this.Controls.Add(btnConnect);
            this.Controls.Add(lblAgent);
            this.Controls.Add(cmbAgents);
            this.Controls.Add(btnRefreshAgents);
            this.Controls.Add(lblPrinter);
            this.Controls.Add(cmbPrinters);
            this.Controls.Add(btnRefreshPrinters);
            this.Controls.Add(lblFile);
            this.Controls.Add(txtFilePath);
            this.Controls.Add(btnSelectFile);
            this.Controls.Add(btnPrint);
            this.Controls.Add(txtLog);
        }

        private void Log(string message)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => Log(message)));
                return;
            }
            txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        }

        private async void BtnConnect_Click(object sender, EventArgs e)
        {
            if (_connection != null && _connection.State == HubConnectionState.Connected)
            {
                await _connection.StopAsync();
                btnConnect.Text = "Connect";
                btnRefreshAgents.Enabled = false;
                btnRefreshPrinters.Enabled = false;
                btnPrint.Enabled = false;
                Log("Disconnected.");
                return;
            }

            string url = txtHubUrl.Text;
            _connection = new HubConnectionBuilder()
                .WithUrl(url)
                .WithAutomaticReconnect()
                .Build();

            _connection.On<List<string>>("ActiveAgentsList", (agents) =>
            {
                Log($"Received ActiveAgentsList: {string.Join(", ", agents)}");
                this.Invoke(new Action(() =>
                {
                    cmbAgents.Items.Clear();
                    foreach (var agent in agents)
                    {
                        cmbAgents.Items.Add(agent);
                    }
                    if (cmbAgents.Items.Count > 0) cmbAgents.SelectedIndex = 0;
                }));
            });

            _connection.On<string, List<string>>("ReceivePrintersList", (sourceAgent, printers) =>
            {
                Log($"Received ReceivePrintersList from {sourceAgent}: {string.Join(", ", printers)}");
                this.Invoke(new Action(() =>
                {
                    cmbPrinters.Items.Clear();
                    foreach (var p in printers)
                    {
                        cmbPrinters.Items.Add(p);
                    }
                    if (cmbPrinters.Items.Count > 0) cmbPrinters.SelectedIndex = 0;
                }));
            });

            _connection.On<string, string>("PrintError", (targetAgent, error) =>
            {
                Log($"Print Error for agent {targetAgent}: {error}");
            });

            _connection.On<bool, string, string>("PrintResult", (isSuccess, docName, message) =>
            {
                string status = isSuccess ? "Success" : "Failed";
                Log($"PrintResult: {status} | Document: {docName} | Message: {message}");
            });

            try
            {
                Log($"Connecting to {url}...");
                await _connection.StartAsync();
                Log("Connected.");
                btnConnect.Text = "Disconnect";
                btnRefreshAgents.Enabled = true;
                btnRefreshPrinters.Enabled = true;
                btnPrint.Enabled = true;
                
                // As a client, we don't need to register as an agent to print, 
                // but we might want to request agents immediately.
                await _connection.InvokeAsync("RequestActiveAgents");
            }
            catch (Exception ex)
            {
                Log($"Connection failed: {ex.Message}");
            }
        }

        private async void BtnRefreshAgents_Click(object sender, EventArgs e)
        {
            if (_connection?.State == HubConnectionState.Connected)
            {
                Log("Requesting active agents...");
                await _connection.InvokeAsync("RequestActiveAgents");
            }
        }

        private async void BtnRefreshPrinters_Click(object sender, EventArgs e)
        {
            if (_connection?.State == HubConnectionState.Connected && cmbAgents.SelectedItem != null)
            {
                string agent = cmbAgents.SelectedItem.ToString();
                Log($"Requesting printers for agent {agent}...");
                await _connection.InvokeAsync("RequestPrinters", agent, _correlationId);
            }
            else
            {
                Log("Please connect and select an agent first.");
            }
        }

        private void BtnSelectFile_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Filter = "All Files (*.*)|*.*";
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    txtFilePath.Text = ofd.FileName;
                }
            }
        }

        private async void BtnPrint_Click(object sender, EventArgs e)
        {
            if (_connection?.State != HubConnectionState.Connected)
            {
                Log("Not connected to hub.");
                return;
            }

            if (cmbAgents.SelectedItem == null || cmbPrinters.SelectedItem == null)
            {
                Log("Please select an agent and a printer.");
                return;
            }

            string agent = cmbAgents.SelectedItem.ToString();
            string printer = cmbPrinters.SelectedItem.ToString();
            string filePath = txtFilePath.Text;

            if (!File.Exists(filePath))
            {
                Log("Selected file does not exist.");
                return;
            }

            try
            {
                Log($"Reading file {Path.GetFileName(filePath)}...");
                byte[] fileBytes = File.ReadAllBytes(filePath);
                string base64Data = Convert.ToBase64String(fileBytes);
                string documentName = Path.GetFileName(filePath);

                Log($"Sending print command to {agent} (Printer: {printer})...");
                await _connection.InvokeAsync("SendPrintCommand", agent, printer, base64Data, documentName);
            }
            catch (Exception ex)
            {
                Log($"Error sending print command: {ex.Message}");
            }
        }
    }
}
