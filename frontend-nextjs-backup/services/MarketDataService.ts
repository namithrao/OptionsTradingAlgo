import { HubConnection, HubConnectionBuilder, LogLevel } from '@microsoft/signalr';

interface MarketEvent {
  type: string;
  symbol: string;
  timestamp: number;
  [key: string]: unknown;
}

interface OptionsChainResponse {
  symbol: string;
  timestamp: string;
  strikes: unknown[];
}

interface SubscriptionResponse {
  message: string;
  success: boolean;
}

interface StrategyValidationResponse {
  isValid: boolean;
  errors: string[];
  config: unknown;
}

interface StrategyTemplatesResponse {
  [key: string]: {
    name: string;
    description: string;
    template: unknown;
  };
}

interface StrategyAnalysisRequest {
  strategyType: string;
  underlyingSymbol: string;
  underlyingPrice: number;
  allocation: number;
  parameters: Record<string, unknown>;
}

interface StrategyAnalysisResponse {
  expectedReturn: number;
  maxRisk: number;
  breakEvenPoints: number[];
  greeks: Record<string, number>;
  probabilityOfProfit: number;
}

interface StockPriceResponse {
  symbol: string;
  price: number;
  timestamp: string;
  success: boolean;
}

export default class MarketDataService {
  private connection: HubConnection;
  private baseUrl: string;

  constructor(baseUrl: string) {
    this.baseUrl = baseUrl;
    this.connection = new HubConnectionBuilder()
      .withUrl(`${baseUrl}/hubs/marketdata`)
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Information)
      .build();
  }

  async connect(): Promise<void> {
    try {
      await this.connection.start();
      console.log('Connected to SignalR hub');
      
      // Set up event listeners
      this.connection.on('MarketEvent', (data: MarketEvent) => {
        console.log('Market event received:', data);
        // Handle market data updates here
        this.handleMarketEvent(data);
      });

    } catch (error) {
      console.error('Error connecting to SignalR hub:', error);
      throw error;
    }
  }

  async disconnect(): Promise<void> {
    if (this.connection) {
      await this.connection.stop();
    }
  }

  async subscribeToSymbol(symbol: string): Promise<void> {
    if (this.connection.state === 'Connected') {
      await this.connection.invoke('SubscribeToSymbol', symbol);
      console.log(`Subscribed to ${symbol}`);
    }
  }

  async unsubscribeFromSymbol(symbol: string): Promise<void> {
    if (this.connection.state === 'Connected') {
      await this.connection.invoke('UnsubscribeFromSymbol', symbol);
      console.log(`Unsubscribed from ${symbol}`);
    }
  }

  private handleMarketEvent(data: MarketEvent): void {
    // Emit custom events that components can listen to
    const event = new CustomEvent('marketEvent', { detail: data });
    window.dispatchEvent(event);
  }

  // REST API methods
  async getOptionsChain(symbol: string): Promise<OptionsChainResponse> {
    const response = await fetch(`${this.baseUrl}/api/marketdata/options-chain/${symbol}`);
    return await response.json();
  }

  async subscribeToMarketData(symbol: string): Promise<SubscriptionResponse> {
    const response = await fetch(`${this.baseUrl}/api/marketdata/subscribe`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({ symbol }),
    });
    return await response.json();
  }

  async validateStrategy(strategyType: string, configYaml: string): Promise<StrategyValidationResponse> {
    const response = await fetch(`${this.baseUrl}/api/strategy/validate`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({ strategyType, configYaml }),
    });
    return await response.json();
  }

  async getStrategyTemplates(): Promise<StrategyTemplatesResponse> {
    const response = await fetch(`${this.baseUrl}/api/strategy/templates`);
    return await response.json();
  }

  async analyzeStrategy(request: StrategyAnalysisRequest): Promise<StrategyAnalysisResponse> {
    const response = await fetch(`${this.baseUrl}/api/strategy/analyze`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify(request),
    });
    return await response.json();
  }

  async getStockPrice(symbol: string): Promise<StockPriceResponse> {
    const response = await fetch(`${this.baseUrl}/api/marketdata/stock-price/${symbol}`);
    return await response.json();
  }
}