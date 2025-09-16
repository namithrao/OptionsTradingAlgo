import * as signalR from '@microsoft/signalr';

export interface MarketData {
  symbol: string;
  time: number;
  open: number;
  high: number;
  low: number;
  close: number;
  volume: number;
}

export interface OptionData {
  symbol: string;
  strike: number;
  expiry: string;
  optionType: 'call' | 'put';
  price: number;
  delta: number;
  gamma: number;
  theta: number;
  vega: number;
  rho: number;
  impliedVolatility: number;
}

export interface BacktestProgress {
  runId: string;
  progress: number;
  currentTime: number;
  equity: number;
  trades: number;
  pnl: number;
}

export class SignalRService {
  private marketConnection: signalR.HubConnection | null = null;
  private backtestConnection: signalR.HubConnection | null = null;

  async connectMarketHub(url: string): Promise<void> {
    this.marketConnection = new signalR.HubConnectionBuilder()
      .withUrl(url + '/markethub')
      .withAutomaticReconnect()
      .build();

    await this.marketConnection.start();
    console.log('Connected to Market Hub');
  }

  async connectBacktestHub(url: string): Promise<void> {
    this.backtestConnection = new signalR.HubConnectionBuilder()
      .withUrl(url + '/backtest')
      .withAutomaticReconnect()
      .build();

    await this.backtestConnection.start();
    console.log('Connected to Backtest Hub');
  }

  onMarketData(callback: (data: MarketData) => void): void {
    if (this.marketConnection) {
      this.marketConnection.on('marketData', callback);
    }
  }

  onOptionData(callback: (data: OptionData) => void): void {
    if (this.marketConnection) {
      this.marketConnection.on('optionData', callback);
    }
  }

  onBacktestProgress(callback: (data: BacktestProgress) => void): void {
    if (this.backtestConnection) {
      this.backtestConnection.on('progress', callback);
    }
  }

  async subscribeToSymbol(symbol: string): Promise<void> {
    if (this.marketConnection) {
      await this.marketConnection.invoke('SubscribeToSymbol', symbol);
    }
  }

  async unsubscribeFromSymbol(symbol: string): Promise<void> {
    if (this.marketConnection) {
      await this.marketConnection.invoke('UnsubscribeFromSymbol', symbol);
    }
  }

  async startBacktest(config: any): Promise<string> {
    if (this.backtestConnection) {
      return await this.backtestConnection.invoke('StartBacktest', config);
    }
    throw new Error('Backtest connection not established');
  }

  async stopBacktest(runId: string): Promise<void> {
    if (this.backtestConnection) {
      await this.backtestConnection.invoke('StopBacktest', runId);
    }
  }

  disconnect(): void {
    if (this.marketConnection) {
      this.marketConnection.stop();
      this.marketConnection = null;
    }
    if (this.backtestConnection) {
      this.backtestConnection.stop();
      this.backtestConnection = null;
    }
  }
}