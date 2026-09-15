/**
 * Line Chart Component — wrapper của Recharts LineChart.
 * Đang dùng: DealerDetail.jsx. Use for: Monthly sales trend, revenue over time
 */

import { LineChart as RechartsLineChart, Line, XAxis, YAxis, CartesianGrid, Tooltip, Legend, ResponsiveContainer } from 'recharts'

const LineChart = ({ data, xKey, yKey, title }) => {
  // Sample data structure:
  // const data = [
  //   { month: 'Jan', sales: 4000 },
  //   { month: 'Feb', sales: 3000 },
  // ]

  return (
    <div className="line-chart">
      {title && <h3>{title}</h3>}
      <ResponsiveContainer width="100%" height={300}>
        <RechartsLineChart data={data}>
          <CartesianGrid strokeDasharray="3 3" />
          <XAxis dataKey={xKey} />
          <YAxis />
          <Tooltip />
          <Legend />
          <Line type="monotone" dataKey={yKey} stroke="#2196f3" />
        </RechartsLineChart>
      </ResponsiveContainer>
    </div>
  )
}

export default LineChart

