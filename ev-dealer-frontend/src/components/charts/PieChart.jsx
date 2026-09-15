/**
 * Pie Chart Component — wrapper của Recharts PieChart.
 * Chưa có importer nào — giữ lại theo quyết định #53 cho dashboard tương lai
 * (top vehicles, sales distribution). Only LineChart is live (DealerDetail).
 */

import { PieChart as RechartsPieChart, Pie, Cell, Tooltip, Legend, ResponsiveContainer } from 'recharts'

const COLORS = ['#2196f3', '#4caf50', '#ff9800', '#f44336', '#9c27b0']

const PieChart = ({ data, nameKey, valueKey, title }) => {
  return (
    <div className="pie-chart">
      {title && <h3>{title}</h3>}
      <ResponsiveContainer width="100%" height={300}>
        <RechartsPieChart>
          <Pie
            data={data}
            dataKey={valueKey}
            nameKey={nameKey}
            cx="50%"
            cy="50%"
            outerRadius={80}
            label
          >
            {data.map((entry, index) => (
              <Cell key={`cell-${index}`} fill={COLORS[index % COLORS.length]} />
            ))}
          </Pie>
          <Tooltip />
          <Legend />
        </RechartsPieChart>
      </ResponsiveContainer>
    </div>
  )
}

export default PieChart

