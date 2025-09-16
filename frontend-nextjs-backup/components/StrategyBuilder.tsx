'use client';

import { useState, useEffect, useCallback } from 'react';
import { Play, Settings, TrendingUp, AlertTriangle } from 'lucide-react';

interface StrategyTemplate {
  name: string;
  description: string;
  template: Record<string, unknown>;
}

interface StrategyValidation {
  isValid: boolean;
  errors: string[];
  config: Record<string, unknown> | null;
}

const StrategyBuilder: React.FC = () => {
  const [selectedStrategy, setSelectedStrategy] = useState('covered-call');
  const [templates, setTemplates] = useState<{ [key: string]: StrategyTemplate }>({});
  const [configYaml, setConfigYaml] = useState('');
  const [validation, setValidation] = useState<StrategyValidation | null>(null);
  const [loading, setLoading] = useState(false);
  const [quickParams, setQuickParams] = useState({
    minDelta: 0.25,
    maxDelta: 0.35,
    minDte: 30,
    maxDte: 45
  });

  const formatYaml = useCallback((obj: Record<string, unknown>): string => {
    // Simple YAML formatter for demo
    const formatObject = (obj: Record<string, unknown>, indent = 0): string => {
      let result = '';
      const spacing = '  '.repeat(indent);
      
      for (const [key, value] of Object.entries(obj)) {
        if (typeof value === 'object' && value !== null && !Array.isArray(value)) {
          result += `${spacing}${key}:\n${formatObject(value as Record<string, unknown>, indent + 1)}`;
        } else {
          result += `${spacing}${key}: ${value}\n`;
        }
      }
      
      return result;
    };
    
    return formatObject(obj);
  }, []);

  const updateConfigWithParams = useCallback((template: Record<string, unknown>) => {
    const updatedTemplate = JSON.parse(JSON.stringify(template)); // Deep clone
    
    // Update parameters based on quickParams
    if (updatedTemplate.strategy && typeof updatedTemplate.strategy === 'object') {
      const strategy = updatedTemplate.strategy as Record<string, unknown>;
      if (strategy.parameters && typeof strategy.parameters === 'object') {
        const params = strategy.parameters as Record<string, unknown>;
        params.min_delta = quickParams.minDelta;
        params.max_delta = quickParams.maxDelta;
        params.min_dte = quickParams.minDte;
        params.max_dte = quickParams.maxDte;
      }
    }
    
    setConfigYaml(formatYaml(updatedTemplate));
  }, [quickParams, formatYaml]);

  const fetchTemplates = useCallback(async () => {
    try {
      const response = await fetch('http://localhost:5002/api/strategy/templates');
      const data = await response.json();
      setTemplates(data);
    } catch (error) {
      console.error('Error fetching templates:', error);
    }
  }, []);

  const updateQuickParam = (key: keyof typeof quickParams, value: number) => {
    setQuickParams(prev => ({ ...prev, [key]: value }));
  };

  useEffect(() => {
    fetchTemplates();
  }, [fetchTemplates]);

  useEffect(() => {
    if (templates[selectedStrategy]) {
      updateConfigWithParams(templates[selectedStrategy].template);
    }
  }, [selectedStrategy, templates, updateConfigWithParams]);

  const validateConfig = async () => {
    if (!configYaml.trim()) return;
    
    setLoading(true);
    try {
      const response = await fetch('http://localhost:5002/api/strategy/validate', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({
          strategyType: selectedStrategy,
          configYaml: configYaml
        }),
      });
      
      const result = await response.json();
      setValidation(result);
    } catch (error) {
      console.error('Error validating strategy:', error);
      setValidation({
        isValid: false,
        errors: ['Failed to validate configuration'],
        config: null
      });
    } finally {
      setLoading(false);
    }
  };

  const analyzeStrategy = async () => {
    if (!validation?.isValid) {
      await validateConfig();
      return;
    }

    try {
      const response = await fetch('http://localhost:5002/api/strategy/analyze', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({
          strategyType: selectedStrategy,
          underlyingSymbol: 'SPY',
          underlyingPrice: 450,
          allocation: 10000,
          parameters: ((validation.config as Record<string, unknown>)?.strategy as Record<string, unknown>)?.parameters || {}
        }),
      });
      
      const analysis = await response.json();
      console.log('Strategy analysis:', analysis);
    } catch (error) {
      console.error('Error analyzing strategy:', error);
    }
  };

  return (
    <div className="space-y-6">
      {/* Strategy Selection */}
      <div>
        <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">
          Strategy Type
        </label>
        <select 
          value={selectedStrategy}
          onChange={(e) => setSelectedStrategy(e.target.value)}
          className="w-full bg-white dark:bg-gray-700 border border-gray-300 dark:border-gray-600 text-gray-900 dark:text-white text-sm rounded-lg focus:ring-blue-500 focus:border-blue-500 block p-2.5"
        >
          {Object.entries(templates).map(([key, template]) => (
            <option key={key} value={key}>{template.name}</option>
          ))}
        </select>
        {templates[selectedStrategy] && (
          <p className="mt-1 text-sm text-gray-500 dark:text-gray-400">
            {templates[selectedStrategy].description}
          </p>
        )}
      </div>

      {/* Configuration Editor */}
      <div>
        <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">
          Configuration (YAML)
        </label>
        <textarea
          value={configYaml}
          onChange={(e) => setConfigYaml(e.target.value)}
          rows={12}
          className="w-full bg-white dark:bg-gray-800 border border-gray-300 dark:border-gray-600 text-gray-900 dark:text-white text-xs font-mono rounded-lg focus:ring-blue-500 focus:border-blue-500 block p-3"
          placeholder="Enter your strategy configuration in YAML format..."
        />
      </div>

      {/* Validation Results */}
      {validation && (
        <div className={`p-4 rounded-lg ${validation.isValid 
          ? 'bg-green-50 dark:bg-green-900/20 border border-green-200 dark:border-green-800' 
          : 'bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800'
        }`}>
          <div className="flex items-center">
            {validation.isValid ? (
              <TrendingUp className="h-5 w-5 text-green-500 mr-2" />
            ) : (
              <AlertTriangle className="h-5 w-5 text-red-500 mr-2" />
            )}
            <h4 className={`font-medium ${validation.isValid ? 'text-green-800 dark:text-green-200' : 'text-red-800 dark:text-red-200'}`}>
              {validation.isValid ? 'Configuration Valid' : 'Configuration Errors'}
            </h4>
          </div>
          {validation.errors.length > 0 && (
            <ul className="mt-2 text-sm text-red-700 dark:text-red-300 list-disc list-inside">
              {validation.errors.map((error, index) => (
                <li key={index}>{error}</li>
              ))}
            </ul>
          )}
        </div>
      )}

      {/* Action Buttons */}
      <div className="flex space-x-3">
        <button
          onClick={validateConfig}
          disabled={loading || !configYaml.trim()}
          className="flex-1 flex items-center justify-center px-4 py-2 text-sm font-medium text-white bg-blue-600 border border-transparent rounded-md hover:bg-blue-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-blue-500 disabled:opacity-50 disabled:cursor-not-allowed"
        >
          <Settings className="h-4 w-4 mr-2" />
          {loading ? 'Validating...' : 'Validate'}
        </button>
        
        <button
          onClick={analyzeStrategy}
          disabled={loading || !validation?.isValid}
          className="flex-1 flex items-center justify-center px-4 py-2 text-sm font-medium text-white bg-green-600 border border-transparent rounded-md hover:bg-green-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-green-500 disabled:opacity-50 disabled:cursor-not-allowed"
        >
          <Play className="h-4 w-4 mr-2" />
          Analyze Strategy
        </button>
      </div>

      {/* Quick Parameters */}
      <div className="border-t border-gray-200 dark:border-gray-700 pt-4">
        <h4 className="text-sm font-medium text-gray-700 dark:text-gray-300 mb-3">Quick Parameters</h4>
        <div className="grid grid-cols-2 gap-4">
          <div>
            <label className="block text-xs text-gray-600 dark:text-gray-400 mb-1">Min Delta</label>
            <input 
              type="number" 
              step="0.01" 
              min="0" 
              max="1" 
              value={quickParams.minDelta}
              onChange={(e) => updateQuickParam('minDelta', parseFloat(e.target.value))}
              className="w-full text-xs bg-white dark:bg-gray-700 border border-gray-300 dark:border-gray-600 rounded px-2 py-1"
            />
          </div>
          <div>
            <label className="block text-xs text-gray-600 dark:text-gray-400 mb-1">Max Delta</label>
            <input 
              type="number" 
              step="0.01" 
              min="0" 
              max="1" 
              value={quickParams.maxDelta}
              onChange={(e) => updateQuickParam('maxDelta', parseFloat(e.target.value))}
              className="w-full text-xs bg-white dark:bg-gray-700 border border-gray-300 dark:border-gray-600 rounded px-2 py-1"
            />
          </div>
          <div>
            <label className="block text-xs text-gray-600 dark:text-gray-400 mb-1">Min DTE</label>
            <input 
              type="number" 
              min="1" 
              max="365" 
              value={quickParams.minDte}
              onChange={(e) => updateQuickParam('minDte', parseInt(e.target.value))}
              className="w-full text-xs bg-white dark:bg-gray-700 border border-gray-300 dark:border-gray-600 rounded px-2 py-1"
            />
          </div>
          <div>
            <label className="block text-xs text-gray-600 dark:text-gray-400 mb-1">Max DTE</label>
            <input 
              type="number" 
              min="1" 
              max="365" 
              value={quickParams.maxDte}
              onChange={(e) => updateQuickParam('maxDte', parseInt(e.target.value))}
              className="w-full text-xs bg-white dark:bg-gray-700 border border-gray-300 dark:border-gray-600 rounded px-2 py-1"
            />
          </div>
        </div>
      </div>
    </div>
  );
};

export default StrategyBuilder;