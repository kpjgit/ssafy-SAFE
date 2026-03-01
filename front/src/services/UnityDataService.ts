// Unity와 프론트엔드 간 실시간 데이터 통신 서비스
import React from "react";

export interface UnityData {
  type: "agent_position" | "sensor_data" | "path_update" | "emergency_alert";
  timestamp: number;
  data: any;
}

export interface AgentPosition {
  agentId: string;
  position: { x: number; y: number; z: number };
  rotation: { x: number; y: number; z: number; w: number };
  state: "walking" | "running" | "idle" | "collapsed";
}

export interface SensorData {
  sensorId: string;
  type: "temperature" | "co2" | "smoke" | "humidity";
  value: number;
  unit: string;
  location: { x: number; y: number; z: number };
}

export interface PathUpdate {
  agentId: string;
  targetId: string;
  path: Array<{ x: number; y: number; z: number }>;
  distance: number;
  estimatedTime: number;
}

export interface EmergencyAlert {
  level: "low" | "medium" | "high" | "critical";
  message: string;
  location: { x: number; y: number; z: number };
  timestamp: number;
}

class UnityDataService {
  private ws: WebSocket | null = null;
  private reconnectAttempts = 0;
  private maxReconnectAttempts = 5;
  private reconnectDelay = 1000;
  private listeners: Map<string, Set<(data: any) => void>> = new Map();

  constructor(private serverUrl: string = "ws://localhost:8080/unity-data") {}

  connect(): Promise<void> {
    return new Promise((resolve, reject) => {
      try {
        this.ws = new WebSocket(this.serverUrl);

        this.ws.onopen = () => {
          console.log("Unity 데이터 서비스 연결됨");
          this.reconnectAttempts = 0;
          resolve();
        };

        this.ws.onmessage = (event) => {
          try {
            const unityData: UnityData = JSON.parse(event.data);
            this.handleMessage(unityData);
          } catch (error) {
            console.error("Unity 데이터 파싱 오류:", error);
          }
        };

        this.ws.onclose = () => {
          console.log("Unity 데이터 서비스 연결 끊김");
          this.attemptReconnect();
        };

        this.ws.onerror = (error) => {
          console.warn("Unity 데이터 서비스 연결 실패 (Unity가 실행되지 않음):", error);
          // Unity가 실행되지 않은 경우에도 resolve하여 앱이 정상 작동하도록 함
          resolve();
        };
      } catch (error) {
        reject(error);
      }
    });
  }

  private attemptReconnect() {
    if (this.reconnectAttempts < this.maxReconnectAttempts) {
      this.reconnectAttempts++;
      console.log(`Unity 데이터 서비스 재연결 시도 ${this.reconnectAttempts}/${this.maxReconnectAttempts}`);
      
      setTimeout(() => {
        this.connect().catch(console.error);
      }, this.reconnectDelay * this.reconnectAttempts);
    } else {
      console.error("Unity 데이터 서비스 최대 재연결 시도 횟수 초과");
    }
  }

  private handleMessage(unityData: UnityData) {
    const listeners = this.listeners.get(unityData.type);
    if (listeners) {
      listeners.forEach(listener => {
        try {
          listener(unityData.data);
        } catch (error) {
          console.error(`Unity 데이터 리스너 오류 (${unityData.type}):`, error);
        }
      });
    }
  }

  // 이벤트 리스너 등록
  subscribe<T>(eventType: string, callback: (data: T) => void): () => void {
    if (!this.listeners.has(eventType)) {
      this.listeners.set(eventType, new Set());
    }
    
    this.listeners.get(eventType)!.add(callback);

    // 구독 해제 함수 반환
    return () => {
      const listeners = this.listeners.get(eventType);
      if (listeners) {
        listeners.delete(callback);
        if (listeners.size === 0) {
          this.listeners.delete(eventType);
        }
      }
    };
  }

  // Unity로 데이터 전송
  sendToUnity(data: any): void {
    if (this.ws && this.ws.readyState === WebSocket.OPEN) {
      this.ws.send(JSON.stringify(data));
    } else {
      console.warn("Unity 데이터 서비스가 연결되지 않음");
    }
  }

  // 특정 Agent 선택 요청
  selectAgent(agentId: string): void {
    this.sendToUnity({
      type: "select_agent",
      agentId: agentId,
      timestamp: Date.now()
    });
  }

  // 경로 계획 요청
  requestPath(agentId: string, targetId: string): void {
    this.sendToUnity({
      type: "request_path",
      agentId: agentId,
      targetId: targetId,
      timestamp: Date.now()
    });
  }

  // 카메라 시점 변경 요청
  changeCameraView(viewType: "overview" | "follow_agent" | "target_focus", targetId?: string): void {
    this.sendToUnity({
      type: "change_camera",
      viewType: viewType,
      targetId: targetId,
      timestamp: Date.now()
    });
  }

  disconnect(): void {
    if (this.ws) {
      this.ws.close();
      this.ws = null;
    }
    this.listeners.clear();
  }

  getConnectionStatus(): "connecting" | "connected" | "disconnected" | "error" {
    if (!this.ws) return "disconnected";
    
    switch (this.ws.readyState) {
      case WebSocket.CONNECTING: return "connecting";
      case WebSocket.OPEN: return "connected";
      case WebSocket.CLOSING:
      case WebSocket.CLOSED: return "disconnected";
      default: return "error";
    }
  }
}

// 싱글톤 인스턴스
export const unityDataService = new UnityDataService();

// React Hook으로 사용하기 위한 커스텀 훅
export function useUnityData<T>(eventType: string, callback: (data: T) => void) {
  const { useEffect } = React;
  
  useEffect(() => {
    const unsubscribe = unityDataService.subscribe(eventType, callback);
    return unsubscribe;
  }, [eventType, callback]);
}
