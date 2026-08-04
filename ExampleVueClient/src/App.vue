<script setup lang="ts">
import { ref, onMounted, onUnmounted } from 'vue';
import * as signalR from '@microsoft/signalr';

// State
const isHubConnected = ref(false);
const connectedAgents = ref<string[]>([]);
const selectedAgent = ref<string | null>(null);
const agentPrinters = ref<string[]>([]);
const isFetchingPrinters = ref(false);
const notifications = ref<{ id: string, message: string, type: 'info'|'success'|'error' }[]>([]);

// Form state
const selectedPrinter = ref('');
const documentName = ref('SampleDocument.txt');
const printType = ref('text'); // text, url, base64
const printData = ref('Hello from Vue PrintAgent Client!');

// Hub Connection
let hubConnection: signalR.HubConnection | null = null;

const addNotification = (message: string, type: 'info' | 'success' | 'error' = 'info') => {
  notifications.value.unshift({
    id: Date.now().toString() + Math.random().toString(),
    message,
    type
  });
  if (notifications.value.length > 50) {
    notifications.value.pop();
  }
};

const connectToHub = async () => {
  // Using localhost:5200 for example, assuming PrintHub runs there. 
  // You might need to change this port to match your ExamplePrintHub launchSettings.json
  const hubUrl = "http://localhost:5000/printhub"; // Update this port as needed

  hubConnection = new signalR.HubConnectionBuilder()
    .withUrl(hubUrl)
    .withAutomaticReconnect()
    .build();

  hubConnection.on("AgentsUpdated", (agents: string[]) => {
    connectedAgents.value = agents;
    if (selectedAgent.value && !agents.includes(selectedAgent.value)) {
      selectedAgent.value = null;
      agentPrinters.value = [];
    }
  });

  hubConnection.on("ReceivePrinters", (agentName: string, printers: string[]) => {
    if (selectedAgent.value === agentName) {
      agentPrinters.value = printers;
      isFetchingPrinters.value = false;
      addNotification(`${agentName} için ${printers.length} yazıcı alındı.`, 'info');
      if (printers.length > 0) {
        selectedPrinter.value = printers[0]; // Auto-select first printer
      }
    }
  });

  hubConnection.on("PrintStatusUpdated", (logId: string, isSuccess: boolean, message: string, docName: string) => {
    addNotification(`[${docName}] Yazdırma ${isSuccess ? 'Başarılı' : 'Başarısız'}: ${message}`, isSuccess ? 'success' : 'error');
  });

  try {
    await hubConnection.start();
    isHubConnected.value = true;
    addNotification('SignalR Hub bağlantısı başarılı.', 'success');
    
    // Register this connection as a UI Client
    await hubConnection.invoke("RegisterUiClient");
  } catch (err) {
    isHubConnected.value = false;
    addNotification('SignalR Hub bağlantısı kurulamadı. Sunucunun çalıştığından emin olun.', 'error');
    console.error(err);
  }
};

const selectAgent = async (agent: string) => {
  selectedAgent.value = agent;
  agentPrinters.value = [];
  isFetchingPrinters.value = true;
  
  if (hubConnection?.state === signalR.HubConnectionState.Connected) {
    try {
      await hubConnection.invoke("RequestPrinters", agent);
      addNotification(`${agent} yazıcı listesi isteniyor...`);
    } catch (err) {
      console.error(err);
      isFetchingPrinters.value = false;
      addNotification('Yazıcı listesi istenirken hata oluştu.', 'error');
    }
  }
};

const sendPrintJob = async () => {
  if (!selectedAgent.value || !selectedPrinter.value || !printData.value) {
    addNotification('Lütfen ajan, yazıcı ve veri seçtiğinizden emin olun.', 'error');
    return;
  }

  if (hubConnection?.state === signalR.HubConnectionState.Connected) {
    try {
      // Data format depends on what PrintAgent expects.
      // The worker expects text, url (http://), base64, or data URI.
      addNotification(`${selectedAgent.value} - ${selectedPrinter.value} yazıcısına komut gönderiliyor...`);
      await hubConnection.invoke("SendPrintJob", selectedAgent.value, selectedPrinter.value, printData.value, documentName.value);
    } catch (err) {
      console.error(err);
      addNotification('Yazdırma komutu gönderilirken hata oluştu.', 'error');
    }
  }
};

onMounted(() => {
  connectToHub();
});

onUnmounted(() => {
  if (hubConnection) {
    hubConnection.stop();
  }
});
</script>

<template>
  <div>
    <h1>PrintAgent Merkezi Kontrol Paneli</h1>
    
    <div class="glass-panel" style="margin-bottom: 2rem; display: flex; justify-content: space-between; align-items: center;">
      <div>
        <strong>Merkezi Sunucu Durumu: </strong>
        <span class="status-badge" :class="isHubConnected ? 'connected' : 'disconnected'">
          {{ isHubConnected ? 'Bağlı' : 'Bağlantı Yok' }}
        </span>
      </div>
      <div v-if="!isHubConnected">
        <small style="color: #94a3b8;">Port: http://localhost:5200</small>
      </div>
    </div>

    <!-- Connected Agents -->
    <div class="glass-panel" style="margin-bottom: 2rem;">
      <h2>Bağlı Ajanlar</h2>
      <p v-if="connectedAgents.length === 0" style="color: #94a3b8;">Şu an bağlı hiçbir PrintAgent bulunmuyor.</p>
      
      <div v-else class="agent-list">
        <div 
          v-for="agent in connectedAgents" 
          :key="agent" 
          class="agent-card"
          :class="{ 'selected': selectedAgent === agent }"
          @click="selectAgent(agent)"
        >
          <div class="agent-name">{{ agent }}</div>
          <div class="status-badge connected">Aktif</div>
        </div>
      </div>
    </div>

    <!-- Print Form -->
    <div v-if="selectedAgent" class="glass-panel">
      <h2>Yazdırma Paneli - {{ selectedAgent }}</h2>
      
      <div class="form-group">
        <label>Hedef Yazıcı</label>
        <select v-model="selectedPrinter" :disabled="isFetchingPrinters">
          <option v-if="isFetchingPrinters" value="">Yazıcılar yükleniyor...</option>
          <option v-else-if="agentPrinters.length === 0" value="">Yazıcı bulunamadı</option>
          <option v-for="printer in agentPrinters" :key="printer" :value="printer">
            {{ printer }}
          </option>
        </select>
      </div>

      <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 1rem;">
        <div class="form-group">
          <label>Belge Adı (İsteğe Bağlı)</label>
          <input type="text" v-model="documentName" placeholder="Örn: Fatura.txt" />
        </div>
        
        <div class="form-group">
          <label>Veri Türü</label>
          <select v-model="printType">
            <option value="text">Düz Metin (Plain Text)</option>
            <option value="url">URL (İnternetten Dosya İndirip Yazdır)</option>
            <option value="base64">Base64 (Dosya Verisi)</option>
          </select>
        </div>
      </div>

      <div class="form-group">
        <label>Yazdırılacak Veri</label>
        <textarea 
          v-model="printData" 
          rows="5" 
          :placeholder="printType === 'url' ? 'http://example.com/file.pdf' : 'Yazdırılacak içerik...'"
        ></textarea>
      </div>

      <button @click="sendPrintJob" :disabled="!selectedPrinter || !printData">
        Belgeyi Yazdır
      </button>
    </div>

    <!-- Notifications -->
    <div class="glass-panel" style="margin-top: 2rem;">
      <h3>Olay Kayıtları</h3>
      <div class="notifications">
        <div v-if="notifications.length === 0" style="color: #94a3b8; font-size: 0.9rem;">
          Henüz bir olay gerçekleşmedi.
        </div>
        <div 
          v-for="notif in notifications" 
          :key="notif.id" 
          class="notification-item"
          :class="notif.type"
        >
          {{ notif.message }}
        </div>
      </div>
    </div>
  </div>
</template>
