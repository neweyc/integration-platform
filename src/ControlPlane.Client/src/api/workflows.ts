import { api } from './client'

export type WorkflowStatus = 'Enabled' | 'Disabled'
export type WorkflowRunStatus = 'Running' | 'Succeeded' | 'Failed'
export type WorkflowNodeRunStatus = 'Pending' | 'Queued' | 'Running' | 'Succeeded' | 'Failed'

export interface WorkflowNode {
  id: string
  key: string
  name: string
  integrationId: string
}

export interface WorkflowEdge {
  from: string
  to: string
}

export interface WorkflowDefinition {
  id: string
  name: string
  slug: string
  environment: string
  status: WorkflowStatus
  nodes: WorkflowNode[]
  edges: WorkflowEdge[]
}

export interface WorkflowNodeRun {
  id: string
  workflowNodeId: string
  nodeKey: string
  nodeName: string
  integrationId: string
  status: WorkflowNodeRunStatus
  workItemId: string | null
  executionRecordId: string | null
}

export interface WorkflowRun {
  id: string
  workflowDefinitionId: string
  status: WorkflowRunStatus
  startedAt: string
  completedAt: string | null
  nodes: WorkflowNodeRun[]
}

export interface ListWorkflowsResponse {
  workflows: WorkflowDefinition[]
}

export interface ListWorkflowRunsResponse {
  runs: WorkflowRun[]
}

export const workflowsApi = {
  list: () => api.get<ListWorkflowsResponse>('/workflows'),
  runs: (workflowId: string, limit = 10) =>
    api.get<ListWorkflowRunsResponse>(`/workflows/${workflowId}/runs?limit=${limit}`),
  run: (workflowId: string) => api.post<WorkflowRun>(`/workflows/${workflowId}/run`, {}),
}
