// src/App.tsx
import VideoStream from "./components/VideoStream";
import UnityHttpVideoStream from "./components/UnityHttpVideoStream";
import React, { useState, useEffect } from "react";

type Sex = "M" | "F";
interface Rescue { id: number; name: string; sex: Sex; injury: string; dest: string; note?: string; }
interface Action { id: number; due: string; withdraw: string; assigned: number; loc: string; note?: string; }

function useIncident(){
  const rescue = [
    { id: 1, name: "홍길동", sex: "M", injury: "경상", dest: "SSAFY병원", note: "의식 명료" },
    { id: 2, name: "김철수", sex: "M", injury: "중상", dest: "한빛병원", note: "산소 투여" },
    { id: 3, name: "이영희", sex: "F", injury: "경상", dest: "서울병원", note: "경미한 화상" },
    { id: 4, name: "박민수", sex: "M", injury: "중상", dest: "강남병원", note: "연기 흡입" },
  ];

  const actions = [
    { id: 1, due: "10:15", withdraw:"11:00", assigned: 4, loc: "2F 북동측 (동측 계단)" },
    { id: 2, due: "10:22", withdraw:"11:10", assigned: 6, loc: "3F 북동측 (로비)" },
    { id: 3, due: "10:35", withdraw:"11:25", assigned: 3, loc: "2F 남동측 (엘리베이터)" },
    { id: 4, due: "10:45", withdraw:"11:40", assigned: 5, loc: "4F 북서측 (계단)" },
  ];

  return { rescue, actions };
}

export default function App(){
  const { rescue: initialRescue, actions: initialActions } = useIncident();

  const [rescue, setRescue] = useState<Rescue[]>(initialRescue);
  const [actions, setActions] = useState<Action[]>(initialActions);
  
  const [unityConnectionStatus, setUnityConnectionStatus] = useState<"connecting" | "connected" | "disconnected" | "error">("disconnected");

  // 모달 상태
  const [modalOpen, setModalOpen] = useState(false);
  const [modalKind, setModalKind] = useState<"rescue"|"action">("rescue");

  // ✅ 수정 모드 제어
  const [editId, setEditId] = useState<number | null>(null);
  const editingRescue = modalKind === "rescue" && editId != null
    ? rescue.find(r => r.id === editId)
    : undefined;
  const editingAction = modalKind === "action" && editId != null
    ? actions.find(a => a.id === editId)
    : undefined;

  // 모달 오픈 핸들러 (추가)
  const openRescueModal = () => { setModalKind("rescue"); setEditId(null); setModalOpen(true); };
  const openActionModal = () => { setModalKind("action"); setEditId(null); setModalOpen(true); };

  // ✅ 행 수정 버튼에서 호출
  const onEditRescueRow = (id: number) => { setModalKind("rescue"); setEditId(id); setModalOpen(true); };
  const onEditActionRow = (id: number) => { setModalKind("action"); setEditId(id); setModalOpen(true); };

  // 추가
  const addRescue = (data: Omit<Rescue,"id">) => {
    setRescue(prev => [...prev, { ...data, id: (prev.at(-1)?.id ?? 0) + 1 }]);
  };
  const addAction = (data: Omit<Action,"id">) => {
    setActions(prev => [...prev, { ...data, id: (prev.at(-1)?.id ?? 0) + 1 }]);
  };

  // ✅ 수정
  const editRescue = (id: number, data: Omit<Rescue,"id">) => {
    setRescue(prev => prev.map(r => r.id === id ? { ...data, id } : r));
  };
  const editAction = (id: number, data: Omit<Action,"id">) => {
    setActions(prev => prev.map(a => a.id === id ? { ...data, id } : a));
  };

  // ✅ 삭제
  const deleteRescue = (id: number) => {
    if (confirm("해당 구조 기록을 삭제할까요?")) {
      setRescue(prev => prev.filter(r => r.id !== id));
    }
  };
  const deleteAction = (id: number) => {
    if (confirm("해당 조치 기록을 삭제할까요?")) {
      setActions(prev => prev.filter(a => a.id !== id));
    }
  };

  // Unity HTTP 서버 연결 확인 (그대로)
  useEffect(() => {
    setUnityConnectionStatus("connecting");
    const checkUnityServer = async () => {
      try {
        const response = await fetch("http://localhost:8081/unity-video/status", {
          method: 'GET',
          mode: 'cors'
        });
        if (response.ok) {
          await response.json();
          setUnityConnectionStatus("connected");
        } else {
          setUnityConnectionStatus("error");
        }
      } catch {
        setUnityConnectionStatus("disconnected");
      }
    };
    checkUnityServer();
    const statusInterval = setInterval(checkUnityServer, 5000);
    return () => { clearInterval(statusInterval); };
  }, []);

  return (
    <div className="min-h-screen bg-neutral-950 text-white p-4 space-y-4">
      <HeaderBar unityStatus={unityConnectionStatus} />
      <div className="grid grid-cols-12 gap-4">
        {/* 좌측 열 - Unity 실시간 스트리밍 */}
        <div className="col-span-12 lg:col-span-6">
          <Card className="h-[500px] overflow-hidden"><UnityMain /></Card>
        </div>

        {/* 우측 열 - 메인 CCTV 영상 */}
        <div className="col-span-12 lg:col-span-6">
          <Card className="h-[500px] overflow-hidden"><UnityTopView /></Card>
        </div>
      </div>
      
      {/* 하단 테이블들 */}
      <div className="grid grid-cols-12 gap-4">
        <div className="col-span-12 lg:col-span-6">
          <Card className="h-[200px] overflow-hidden">
            <RescueTable
              rows={rescue}
              onAdd={openRescueModal}
              onEdit={onEditRescueRow}     // ✅ 연결
              onDelete={deleteRescue}      // ✅ 연결
            />
          </Card>
        </div>
        <div className="col-span-12 lg:col-span-6">
          <Card className="h-[200px] overflow-hidden">
            <ActionTable
              rows={actions}
              onAdd={openActionModal}
              onEdit={onEditActionRow}     // ✅ 연결
              onDelete={deleteAction}      // ✅ 연결
            />
          </Card>
        </div>
      </div>

      {/* 모달 렌더 */}
      <Modal
        open={modalOpen}
        onClose={() => setModalOpen(false)}
        title={
          modalKind === "rescue"
            ? (editId == null ? "구조 인원 추가" : "구조 인원 수정")
            : (editId == null ? "조치 현황 추가" : "조치 현황 수정")
        }
      >
        {modalKind === "rescue" ? (
          <RescueForm
            initialData={editingRescue}   // ✅ 수정 시 초기값
            onSubmit={(data) => {
              if (editId == null) addRescue(data);
              else editRescue(editId, data);
              setModalOpen(false);
            }}
            onCancel={() => setModalOpen(false)}
          />
        ) : (
          <ActionForm
            initialData={editingAction}    // ✅ 수정 시 초기값
            onSubmit={(data) => {
              if (editId == null) addAction(data);
              else editAction(editId, data);
              setModalOpen(false);
            }}
            onCancel={() => setModalOpen(false)}
          />
        )}
      </Modal>
    </div>
  );
}

/* --- 공통 컴포넌트 --- */
function HeaderBar({ unityStatus }: { unityStatus: string }){
  return (
    <div className="flex items-center gap-6">
      <div className="px-4 py-2 bg-neutral-800 rounded-xl font-bold">S.A.F.E</div>
      <Info label="화재 발생 구역" value="2F 북동측" />
      <Info label="화재 발생" value="10:07" />
      <Info label="예상 요구조자 수" value="24명" />
      <div className="flex items-center gap-2">
        <div className={`w-2 h-2 rounded-full ${
          unityStatus === "connected" ? "bg-green-400" :
          unityStatus === "connecting" ? "bg-yellow-400 animate-pulse" :
          unityStatus === "error" ? "bg-red-400" : "bg-gray-400"
        }`} />
        <span className="text-xs text-neutral-400">Unity {unityStatus}</span>
      </div>
    </div>
  );
}
const Info = ({label, value}:{label:string; value:string})=> (
  <div className="text-sm text-neutral-300">
    <span className="mr-2 text-neutral-400">{label}</span>
    <span className="font-medium text-white">{value}</span>
  </div>
);
const Card = ({children, className=""}:{children:React.ReactNode; className?:string}) =>
  <div className={`bg-neutral-900 rounded-2xl p-3 shadow-inner border border-neutral-800 ${className}`}>{children}</div>;

function UnityMain(){ 
  return (
    <div className="h-full flex flex-col">
      <div className="text-sm mb-2 flex items-center justify-between">
        <span>Unity 시뮬레이션 (실시간)</span>
        <div className="flex items-center gap-2 text-xs text-neutral-400">
          <span>HTTP Stream</span>
          <span>•</span>
          <span>Port 8081</span>
        </div>
      </div>
      <div className="flex-1 overflow-hidden">
        <UnityHttpVideoStream 
          streamUrl="http://localhost:8081/unity-video"
          aspect={16/9}
          className="w-full h-full object-cover"
          showCameraControls={true}
        />
      </div>
    </div>
  );
}

function UnityTopView(){ 
  return (
    <div className="h-full flex flex-col">
      <div className="text-sm mb-2 flex items-center justify-between">
        <span>실제 촬영 화면 (CCTV)</span>
        <div className="flex items-center gap-2 text-xs text-neutral-400">
          <span>Python Stream</span>
          <span>•</span>
          <span>YOLO Pose</span>
        </div>
      </div>
      <div className="flex-1 overflow-hidden">
        <VideoStream 
          streamType="python"
          aspect={16/9}
          className="w-full h-full object-cover"
          fallbackText="Python 스트림 연결 중..."
        />
      </div>
    </div>
  );
}

function RescueTable({
  rows, onAdd, onEdit, onDelete
}:{ rows: Rescue[]; onAdd: ()=>void; onEdit:(id:number)=>void; onDelete:(id:number)=>void }){
  return (
    <Table
      title="실시간 구조 현황"
      columns={["#", "성명", "성별", "부상정도", "이송병원", "특이사항", ""]} // 마지막 "" = 액션
      renderRow={(r:Rescue,i:number)=>[
        i+1,
        r.name,
        r.sex==="F"?"여성":"남성",
        r.injury,
        r.dest,
        r.note ?? "",
        <RowActions key={`act-${r.id}`} onEdit={()=>onEdit(r.id)} onDelete={()=>onDelete(r.id)} />
      ]}
      rows={rows}
      addButtonLabel="추가하기"
      onAdd={onAdd}   
    />
  );
}

function ActionTable({
  rows, onAdd, onEdit, onDelete
}:{ rows: Action[]; onAdd: ()=>void; onEdit:(id:number)=>void; onDelete:(id:number)=>void }){
  return (
    <Table
      title="실시간 조치 현황"
      columns={["투입시간","철수예정","투입인원","작업위치(진입경로)","특이사항",""]} // 마지막 "" = 액션
      renderRow={(r:Action)=>[
        r.due,
        r.withdraw,
        r.assigned,
        r.loc,
        r.note ?? "",
        <RowActions key={`act-${r.id}`} onEdit={()=>onEdit(r.id)} onDelete={()=>onDelete(r.id)} />
      ]}
      rows={rows}
      addButtonLabel="추가하기"
      onAdd={onAdd}  
    />
  );
}

function RowActions({ onEdit, onDelete }:{ onEdit:()=>void; onDelete:()=>void }) {
  return (
    <div className="flex gap-2 justify-end pr-2 whitespace-nowrap">
      <button
        className="px-2 py-0.5 rounded-md text-[11px] bg-neutral-800 hover:bg-neutral-700"
        onClick={onEdit}
      >
        수정
      </button>
      <button
        className="px-2 py-0.5 rounded-md text-[11px] bg-red-600 hover:bg-red-500"
        onClick={onDelete}
      >
        삭제
      </button>
    </div>
  );
}

function Table({title, columns, rows, renderRow, addButtonLabel, onAdd}:
  {title:string; columns:string[]; rows:any[]; renderRow:(r:any, i:number)=>React.ReactNode[]; addButtonLabel:string; onAdd?: ()=>void}){
  return (
    <div className="h-full flex flex-col">
      <div className="flex items-center justify-between mb-2 flex-shrink-0">
        <div className="text-sm">{title}</div>
        <button
          className="text-xs bg-sky-600 hover:bg-sky-500 px-2 py-1 rounded-md"
          onClick={onAdd}
        >
          {addButtonLabel}
        </button>
      </div>
      <div className="flex-1 overflow-y-auto overflow-x-hidden scrollbar-dark">
  <table className="w-full text-xs table-fixed"> {/* table-fixed로 열 너비 고정 */}
          <thead className="sticky top-0 bg-neutral-900 z-10">
            <tr>
              {columns.map((c, idx) => (
                <th
                  key={idx}
                  className={
                    "text-left font-normal text-neutral-400 px-2 py-1 " +
                    (idx === columns.length - 1 ? "w-24 text-right" : "") // 액션열 고정폭+우측정렬
                  }
                >
                  {c}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {rows.map((r,i)=>(
              <tr key={r.id ?? i} className="border-t border-neutral-800">
                {renderRow(r,i).map((cell,idx)=>(
                  <td
                    key={idx}
                    className={
                      "px-2 py-1 align-middle " +
                      (idx === columns.length - 1 ? "text-right" : "")
                    }
                  >
                    {cell}
                  </td>
                ))}
              </tr>
            ))}
            {!rows.length && (
              <tr>
                <td className="px-2 py-6 text-neutral-500" colSpan={columns.length}>
                  데이터 없음
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}

function Modal({
  open, onClose, title, children,
}: { open:boolean; onClose:()=>void; title?:string; children:React.ReactNode }) {
  if (!open) return null;
  return (
    <div className="fixed inset-0 z-50" role="dialog" aria-modal>
      <div className="absolute inset-0 bg-black/60" onClick={onClose} />
      <div className="absolute inset-0 flex items-center justify-center p-4">
        <div className="w-full max-w-md rounded-2xl bg-neutral-900 border border-neutral-800 shadow-xl">
          <div className="px-4 py-3 border-b border-neutral-800 flex items-center justify-between">
            <h3 className="text-sm font-semibold">{title}</h3>
            <button className="text-neutral-400 hover:text-white" onClick={onClose} aria-label="닫기">×</button>
          </div>
          <div className="p-4">{children}</div>
        </div>
      </div>
    </div>
  );
}

function Field({label, children}:{label:string; children:React.ReactNode}) {
  return (
    <label className="block text-xs mb-3">
      <div className="mb-1 text-neutral-300">{label}</div>
      {children}
    </label>
  );
}

function RescueForm({
  onSubmit, onCancel, initialData,
}: {
  onSubmit:(data: Omit<Rescue,"id">)=>void;
  onCancel:()=>void;
  initialData?: Rescue;
}) {
  const [name, setName] = useState(initialData?.name ?? "");
  const [sex, setSex] = useState<Sex>(initialData?.sex ?? "M");
  const [injury, setInjury] = useState<string>(initialData?.injury ?? "경상");
  const [dest, setDest] = useState(initialData?.dest ?? "");
  const [note, setNote] = useState(initialData?.note ?? "");
  const valid = name.trim() && dest.trim();
  const isEdit = Boolean(initialData);

  return (
    <form onSubmit={(e)=>{ e.preventDefault(); if(!valid) return; onSubmit({ name:name.trim(), sex, injury, dest:dest.trim(), note:note.trim()||undefined }); }}>
      <Field label="성명">
        <input className="w-full rounded-md bg-neutral-800 border border-neutral-700 px-2 py-1"
               value={name} onChange={e=>setName(e.target.value)} placeholder="예) 홍길동" />
      </Field>
      <div className="grid grid-cols-2 gap-3">
        <Field label="성별">
          <select className="w-full rounded-md bg-neutral-800 border border-neutral-700 px-2 py-1"
                  value={sex} onChange={e=>setSex(e.target.value as Sex)}>
            <option value="M">남성</option>
            <option value="F">여성</option>
          </select>
        </Field>
        <Field label="부상 정도">
          <select className="w-full rounded-md bg-neutral-800 border border-neutral-700 px-2 py-1"
                  value={injury} onChange={e=>setInjury(e.target.value)}>
            <option value="경상">경상</option>
            <option value="중상">중상</option>
          </select>
        </Field>
      </div>
      <Field label="이송 병원">
        <input className="w-full rounded-md bg-neutral-800 border border-neutral-700 px-2 py-1"
               value={dest} onChange={e=>setDest(e.target.value)} placeholder="예) SSAFY병원" />
      </Field>
      <Field label="특이사항 (선택)">
        <input className="w-full rounded-md bg-neutral-800 border border-neutral-700 px-2 py-1"
               value={note} onChange={e=>setNote(e.target.value)} placeholder="예) 의식 명료" />
      </Field>
      <div className="mt-4 flex justify-end gap-2">
        <button type="button" onClick={onCancel} className="px-3 py-1 rounded-md bg-neutral-800 hover:bg-neutral-700 text-xs">취소</button>
        <button disabled={!valid} className={`px-3 py-1 rounded-md text-xs ${valid ? "bg-sky-600 hover:bg-sky-500" : "bg-neutral-700 text-neutral-400 cursor-not-allowed"}`}>
          {isEdit ? "수정" : "추가"}
        </button>
      </div>
    </form>
  );
}

function ActionForm({
  onSubmit, onCancel, initialData,
}: {
  onSubmit:(data: Omit<Action,"id">)=>void;
  onCancel:()=>void;
  initialData?: Action;
}) {
  const now = new Date(); const hh = String(now.getHours()).padStart(2,"0"); const mm = String(now.getMinutes()).padStart(2,"0");
  const [due, setDue] = useState(initialData?.due ?? `${hh}:${mm}`);
  const [withdraw, setWithdraw] = useState(initialData?.withdraw ?? `${hh}:${mm}`);
  const [assigned, setAssigned] = useState(initialData?.assigned ?? 3);
  const [loc, setLoc] = useState(initialData?.loc ?? "");
  const [note, setNote] = useState(initialData?.note ?? "");
  const valid = /^\d{2}:\d{2}$/.test(due) && assigned > 0 && loc.trim();
  const isEdit = Boolean(initialData);

  return (
    <form onSubmit={(e)=>{ e.preventDefault(); if(!valid) return; onSubmit({ due, withdraw, assigned, loc:loc.trim(), note:note.trim()||undefined }); }}>
      <div className="grid grid-cols-2 gap-3">
        <Field label="투입 시간 (HH:mm)">
          <input className="w-full rounded-md bg-neutral-800 border border-neutral-700 px-2 py-1"
                 value={due} onChange={e=>setDue(e.target.value)} placeholder="예) 10:30" />
        </Field>
        <Field label="철수 예정 (HH:mm)">
          <input className="w-full rounded-md bg-neutral-800 border border-neutral-700 px-2 py-1"
                 value={withdraw} onChange={e=>setWithdraw(e.target.value)} placeholder="예) 11:30" />
        </Field>
        <Field label="투입 인원">
          <input type="number" min={1} className="w-full rounded-md bg-neutral-800 border border-neutral-700 px-2 py-1"
                 value={assigned} onChange={e=>setAssigned(Number(e.target.value))} />
        </Field>
      </div>
      <Field label="작업 위치(진입 경로)">
        <input className="w-full rounded-md bg-neutral-800 border border-neutral-700 px-2 py-1"
               value={loc} onChange={e=>setLoc(e.target.value)} placeholder="예) 2F 북동측 (동측 계단)" />
      </Field>
      <Field label="특이사항 (선택)">
        <input className="w-full rounded-md bg-neutral-800 border border-neutral-700 px-2 py-1"
               value={note} onChange={e=>setNote(e.target.value)} />
      </Field>
      <div className="mt-4 flex justify-end gap-2">
        <button type="button" onClick={onCancel} className="px-3 py-1 rounded-md bg-neutral-800 hover:bg-neutral-700 text-xs">취소</button>
        <button disabled={!valid} className={`px-3 py-1 rounded-md text-xs ${valid ? "bg-sky-600 hover:bg-sky-500" : "bg-neutral-700 text-neutral-400 cursor-not-allowed"}`}>
          {isEdit ? "수정" : "추가"}
        </button>
      </div>
    </form>
  );
}
