/**
 * Bar Chart Component — wrapper của Recharts BarChart.
 * Chưa có importer nào — giữ lại theo quyết định #53 cho dashboard tương lai
 * (sales by region, sales by dealer). Only LineChart is live (DealerDetail).
 */

import { BarChart as RechartsBarChart, Bar, XAxis, YAxis, CartesianGrid, Tooltip, Legend, ResponsiveContainer } from 'recharts'

const BarChart = ({ data, xKey, yKey, title }) => {
  return (
    <div className="bar-chart">
      {title && <h3>{title}</h3>}
      <ResponsiveContainer width="100%" height={300}>
        <RechartsBarChart data={data}>
          <CartesianGrid strokeDasharray="3 3" />
          <XAxis dataKey={xKey} />
          <YAxis />
          <Tooltip />
          <Legend />
          <Bar dataKey={yKey} fill="#2196f3" />
        </RechartsBarChart>
      </ResponsiveContainer>
    </div>
  )
}

export default BarChart

