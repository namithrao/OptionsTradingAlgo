import React, { useEffect, useRef } from 'react';
import { createChart } from 'lightweight-charts';
import type { IChartApi, ISeriesApi, CandlestickData, LineData } from 'lightweight-charts';

interface TradingChartProps {
  data: CandlestickData[];
  optionData?: LineData[];
  width?: number;
  height?: number;
}

export const TradingChart: React.FC<TradingChartProps> = ({ 
  data, 
  optionData, 
  width = 800, 
  height = 400 
}) => {
  const chartContainerRef = useRef<HTMLDivElement>(null);
  const chartRef = useRef<IChartApi | null>(null);
  const candlestickSeriesRef = useRef<ISeriesApi<'Candlestick'> | null>(null);
  const optionSeriesRef = useRef<ISeriesApi<'Line'> | null>(null);

  useEffect(() => {
    if (!chartContainerRef.current) return;

    // Create chart
    const chart = createChart(chartContainerRef.current, {
      width,
      height,
      layout: {
        background: { color: '#ffffff' },
        textColor: '#333',
      },
      grid: {
        vertLines: { color: '#f0f0f0' },
        horzLines: { color: '#f0f0f0' },
      },
      crosshair: {
        mode: 1,
      },
      rightPriceScale: {
        borderColor: '#cccccc',
      },
      timeScale: {
        borderColor: '#cccccc',
        timeVisible: true,
        secondsVisible: false,
      },
    });

    chartRef.current = chart;

    // Add candlestick series
    const candlestickSeries = chart.addSeries('Candlestick', {
      upColor: '#22c55e',
      downColor: '#ef4444',
      borderDownColor: '#ef4444',
      borderUpColor: '#22c55e',
      wickDownColor: '#ef4444',
      wickUpColor: '#22c55e',
    });

    candlestickSeriesRef.current = candlestickSeries;

    // Add option series if data is provided
    if (optionData) {
      const optionSeries = chart.addSeries('Line', {
        color: '#3b82f6',
        lineWidth: 2,
        priceScaleId: 'right',
      });
      optionSeriesRef.current = optionSeries;
      optionSeries.setData(optionData);
    }

    // Handle resize
    const handleResize = () => {
      if (chartContainerRef.current) {
        chart.applyOptions({
          width: chartContainerRef.current.clientWidth,
        });
      }
    };

    window.addEventListener('resize', handleResize);

    return () => {
      window.removeEventListener('resize', handleResize);
      chart.remove();
    };
  }, [width, height]);

  useEffect(() => {
    if (candlestickSeriesRef.current && data.length > 0) {
      candlestickSeriesRef.current.setData(data);
    }
  }, [data]);

  useEffect(() => {
    if (optionSeriesRef.current && optionData && optionData.length > 0) {
      optionSeriesRef.current.setData(optionData);
    }
  }, [optionData]);

  return <div ref={chartContainerRef} className="trading-chart-container" />;
};