/**
 * Enhanced Table Component
 * Supports sorting, filtering, pagination, and custom cell rendering
 */

import { useState, useMemo } from 'react'

const Table = ({ 
  columns, 
  data, 
  onRowClick,
  sortable = true,
  searchable = true,
  pagination = false,
  pageSize = 10,
  className = '',
  emptyMessage = 'No data available',
  loading = false
}) => {
  const [sortConfig, setSortConfig] = useState({ key: null, direction: 'asc' })
  const [searchTerm, setSearchTerm] = useState('')
  const [currentPage, setCurrentPage] = useState(1)

  // Filter and search data
  const filteredData = useMemo(() => {
    let filtered = data

    if (searchable && searchTerm) {
      filtered = filtered.filter(row =>
        columns.some(col => {
          const value = row[col.key]
          return value && value.toString().toLowerCase().includes(searchTerm.toLowerCase())
        })
      )
    }

    return filtered
  }, [data, searchTerm, columns, searchable])

  // Sort data
  const sortedData = useMemo(() => {
    if (!sortConfig.key) return filteredData

    return [...filteredData].sort((a, b) => {
      const aValue = a[sortConfig.key]
      const bValue = b[sortConfig.key]

      if (aValue < bValue) {
        return sortConfig.direction === 'asc' ? -1 : 1
      }
      if (aValue > bValue) {
        return sortConfig.direction === 'asc' ? 1 : -1
      }
      return 0
    })
  }, [filteredData, sortConfig])

  // Paginate data
  const paginatedData = useMemo(() => {
    if (!pagination) return sortedData

    const startIndex = (currentPage - 1) * pageSize
    const endIndex = startIndex + pageSize
    return sortedData.slice(startIndex, endIndex)
  }, [sortedData, currentPage, pageSize, pagination])

  const handleSort = (key) => {
    if (!sortable) return

    setSortConfig(prev => ({
      key,
      direction: prev.key === key && prev.direction === 'asc' ? 'desc' : 'asc'
    }))
  }

  const getSortIcon = (key) => {
    if (sortConfig.key !== key) return '↕'
    return sortConfig.direction === 'asc' ? '↑' : '↓'
  }

  const totalPages = Math.ceil(filteredData.length / pageSize)

  const tableClasses = [
    'table-container',
    className
  ].filter(Boolean).join(' ')

  if (loading) {
    return (
      <div className={tableClasses}>
        <div className="table-loading">
          <div className="spinner"></div>
          <p>Loading data...</p>
        </div>
      </div>
    )
  }

  return (
    <div className={tableClasses}>
      {searchable && (
        <div className="table-search">
          <input
            type="text"
            placeholder="Search..."
            value={searchTerm}
            onChange={(e) => setSearchTerm(e.target.value)}
            className="table-search-input"
          />
        </div>
      )}

      <div className="table-wrapper">
        <table className="table">
          <thead className="table-header">
            <tr>
              {columns.map((col) => (
                <th 
                  key={col.key}
                  className={`table-header-cell ${sortable && col.sortable !== false ? 'sortable' : ''}`}
                  onClick={() => col.sortable !== false && handleSort(col.key)}
                >
                  <div className="table-header-content">
                    <span className="table-header-label">{col.label}</span>
                    {sortable && col.sortable !== false && (
                      <span className="table-sort-icon">{getSortIcon(col.key)}</span>
                    )}
                  </div>
                </th>
              ))}
            </tr>
          </thead>
          <tbody className="table-body">
            {paginatedData.length > 0 ? (
              paginatedData.map((row, index) => (
                <tr 
                  key={index} 
                  className="table-row"
                  onClick={() => onRowClick?.(row)}
                >
                  {columns.map((col) => (
                    <td key={col.key} className="table-cell">
                      {col.render ? col.render(row[col.key], row, index) : row[col.key]}
                    </td>
                  ))}
                </tr>
              ))
            ) : (
              <tr>
                <td colSpan={columns.length} className="table-empty">
                  {emptyMessage}
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>

      {pagination && totalPages > 1 && (
        <div className="table-pagination">
          <button 
            onClick={() => setCurrentPage(prev => Math.max(prev - 1, 1))}
            disabled={currentPage === 1}
            className="pagination-btn"
          >
            Previous
          </button>
          
          <span className="pagination-info">
            Page {currentPage} of {totalPages}
          </span>
          
          <button 
            onClick={() => setCurrentPage(prev => Math.min(prev + 1, totalPages))}
            disabled={currentPage === totalPages}
            className="pagination-btn"
          >
            Next
          </button>
        </div>
      )}
    </div>
  )
}

export default Table

