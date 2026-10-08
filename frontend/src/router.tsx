import { createBrowserRouter, Navigate } from 'react-router'
import { Layout } from './App'
import { ConnectionsPage } from './pages/ConnectionsPage'
import { ProjectGanttPage } from './pages/ProjectGanttPage'
import { ProjectPage } from './pages/ProjectPage'
import { ProjectsPage } from './pages/ProjectsPage'
import { WorkloadPage } from './pages/WorkloadPage'

export const router = createBrowserRouter([
  {
    element: <Layout />,
    children: [
      { index: true, element: <Navigate to="/projects" replace /> },
      { path: 'projects', element: <ProjectsPage /> },
      { path: 'projects/:id', element: <ProjectPage /> },
      { path: 'projects/:id/gantt', element: <ProjectGanttPage /> },
      { path: 'workload', element: <WorkloadPage /> },
      { path: 'workload/:userId', element: <WorkloadPage /> },
      { path: 'connections', element: <ConnectionsPage /> },
      { path: '*', element: <Navigate to="/projects" replace /> },
    ],
  },
])
