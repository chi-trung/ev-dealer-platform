import React, { useState } from 'react';
import {
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Paper,
  TablePagination,
  TextField,
  InputAdornment,
  Box,
  Typography,
  Chip,
  IconButton,
  Tooltip,
  Menu,
  MenuItem,
  Checkbox,
  FormControlLabel,
  Stack,
  Button,
  Avatar
} from '@mui/material';
import {
  Search as SearchIcon,
  FilterList as FilterIcon,
  MoreVert as MoreVertIcon,
  Edit as EditIcon,
  Delete as DeleteIcon,
  Visibility as ViewIcon,
  Download as DownloadIcon,
  Share as ShareIcon
} from '@mui/icons-material';

const DataTable = ({
  columns = [],
  data = [],
  title = '',
  searchable = true,
  pagination = true,
  selectable = false,
  actions = [],
  onRowClick,
  onSelectionChange,
  className = ''
}) => {
  const [page, setPage] = useState(0);
  const [rowsPerPage, setRowsPerPage] = useState(10);
  const [searchTerm, setSearchTerm] = useState('');
  const [selectedRows, setSelectedRows] = useState([]);
  const [anchorEl, setAnchorEl] = useState(null);
  const [, setSelectedRow] = useState(null);

  // Filter data based on search term
  const filteredData = data.filter(row =>
    Object.values(row).some(value =>
      String(value).toLowerCase().includes(searchTerm.toLowerCase())
    )
  );

  // Pagination
  const paginatedData = pagination
    ? filteredData.slice(page * rowsPerPage, page * rowsPerPage + rowsPerPage)
    : filteredData;

  const handleChangePage = (event, newPage) => {
    setPage(newPage);
  };

  const handleChangeRowsPerPage = (event) => {
    setRowsPerPage(parseInt(event.target.value, 10));
    setPage(0);
  };

  const handleSelectAllClick = (event) => {
    if (event.target.checked) {
      const newSelected = paginatedData.map((row) => row.id);
      setSelectedRows(newSelected);
      onSelectionChange?.(newSelected);
    } else {
      setSelectedRows([]);
      onSelectionChange?.([]);
    }
  };

  const handleRowClick = (event, row) => {
    // Prevent navigation when clicking on interactive elements
    if (event.target.closest('button, a, input')) {
      return;
    }
    onRowClick?.(row);
  };

  const handleSelectRow = (rowId) => {
    const newSelected = selectedRows.includes(rowId)
      ? selectedRows.filter(id => id !== rowId)
      : [...selectedRows, rowId];
    
    setSelectedRows(newSelected);
    onSelectionChange?.(newSelected);
  };

  const handleActionClick = (event, row) => {
    setAnchorEl(event.currentTarget);
    setSelectedRow(row);
  };

  const handleActionClose = () => {
    setAnchorEl(null);
    setSelectedRow(null);
  };

  const renderStatus = (status) => {
    const statusConfig = {
      active: { color: 'success.main', label: 'Hoạt động' },
      inactive: { color: 'error.main', label: 'Tạm ngưng' },
      pending: { color: 'warning.main', label: 'Chờ xử lý' },
      completed: { color: 'success.main', label: 'Hoàn thành' },
      cancelled: { color: 'error.main', label: 'Đã hủy' }
    };

    const config = statusConfig[status?.toLowerCase()] || { color: 'text.secondary', label: status };
    return (
      <Typography variant="body2" sx={{ color: config.color, fontWeight: 500 }}>
        {config.label}
      </Typography>
    );
  };

  const getAvatarChar = (name) => {
    if (!name) return '';
    const nameParts = name.split(' ');
    return nameParts[nameParts.length - 1].charAt(0).toUpperCase();
  }

  const renderCellContent = (column, row) => {
    // Support valueGetter function for computed values
    const value = column.valueGetter 
      ? column.valueGetter({ row, field: column.field }) 
      : row[column.field];

    // THÊM ĐOẠN CODE NÀY:
    // Kiểm tra nếu có hàm renderCell tùy chỉnh được cung cấp cho cột
    if (column.renderCell) {
      // Truyền các tham số cần thiết cho hàm renderCell tùy chỉnh
      return column.renderCell({ value, row, field: column.field });
    }
    // KẾT THÚC ĐOẠN CODE THÊM VÀO

    switch (column.type) {
      case 'avatar':
        return (
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
            <Avatar
              src={row.avatar?.src}
              sx={{ width: 32, height: 32 }}
            >
              {getAvatarChar(row.name)}
            </Avatar>
            <Typography variant="body2" sx={{ fontWeight: 500 }}>
              {row.name}
            </Typography>
          </Box>
        );
      
      case 'status':
        return renderStatus(value);
      
      case 'chip':
        return (
          <Chip
            label={value}
            color={column.color || 'primary'}
            size="small"
            sx={{ fontWeight: 600 }}
          />
        );
      
      case 'actions':
        return (
          <Stack direction="row" spacing={0.5}>
            {actions.map((action, index) => (
              <Tooltip key={index} title={action.tooltip}>
                <IconButton
                  size="small"
                  onClick={(e) => {
                    e.stopPropagation();
                    action.onClick(row);
                  }}
                  sx={{ color: action.color || 'primary.main' }}
                >
                  {action.icon}
                </IconButton>
              </Tooltip>
            ))}
            <Tooltip title="Thêm">
              <IconButton
                size="small"
                onClick={(e) => handleActionClick(e, row)}
                sx={{ color: 'text.secondary' }}
              >
                <MoreVertIcon />
              </IconButton>
            </Tooltip>
          </Stack>
        );
      
      case 'number':
        return (
          <Typography variant="body2" sx={{ fontWeight: 600, color: 'text.primary' }}>
            {typeof value === 'number' ? value.toLocaleString() : value}
          </Typography>
        );
      
      default:
        if (column.field === 'phone') {
          return (
            <Typography variant="body2" sx={{ color: 'text.primary' }}>
              {String(value).replace(/-/g, '')}
            </Typography>
          );
        }
        // Handle objects and arrays
        if (value && typeof value === 'object') {
          if (Array.isArray(value)) {
            return (
              <Typography variant="body2" sx={{ color: 'text.primary' }}>
                {value.length} items
              </Typography>
            );
          }
          return (
            <Typography variant="body2" sx={{ color: 'text.primary' }}>
              {JSON.stringify(value)}
            </Typography>
          );
        }
        
        return (
          <Typography variant="body2" sx={{ color: 'text.primary' }}>
            {value}
          </Typography>
        );
    }
  };

  return (
    <Box className={`data-table fade-in ${className}`}>
      {/* Table Header */}
      {title && (
        <Box sx={{ mb: 2 }}>
          <Typography variant="h6" sx={{ fontWeight: 600, color: 'text.primary' }}>
            {title}
          </Typography>
        </Box>
      )}

      {/* Search Bar */}
      {searchable && (
        <Box sx={{ mb: 2, display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
          <TextField
            placeholder="Tìm kiếm..."
            value={searchTerm}
            onChange={(e) => setSearchTerm(e.target.value)}
            size="small"
            sx={{ width: 300 }}
            InputProps={{
              startAdornment: (
                <InputAdornment position="start">
                  <SearchIcon color="action" />
                </InputAdornment>
              ),
            }}
          />
          <Stack direction="row" spacing={1}>
            <Button
              variant="outlined"
              startIcon={<FilterIcon />}
              size="small"
            >
              Bộ lọc
            </Button>
            <Button
              variant="outlined"
              startIcon={<DownloadIcon />}
              size="small"
            >
              Xuất
            </Button>
          </Stack>
        </Box>
      )}

      {/* Table */}
      <TableContainer 
        component={Paper} 
        sx={{ 
          borderRadius: 2,
          boxShadow: '0px 1px 3px rgba(0, 0, 0, 0.1), 0px 1px 2px rgba(0, 0, 0, 0.06)',
          overflow: 'hidden'
        }}
      >
        <Table>
          <TableHead>
            <TableRow sx={{ backgroundColor: 'grey.50' }}>
              {selectable && (
                <TableCell padding="checkbox">
                  <Checkbox
                    indeterminate={selectedRows.length > 0 && selectedRows.length < paginatedData.length}
                    checked={paginatedData.length > 0 && selectedRows.length === paginatedData.length}
                    onChange={handleSelectAllClick}
                  />
                </TableCell>
              )}
              {columns.map((column) => (
                <TableCell
                  key={column.field}
                  sx={{
                    fontWeight: 600,
                    color: 'text.primary',
                    fontSize: '0.875rem',
                    backgroundColor: 'grey.50',
                    borderBottom: '1px solid',
                    borderColor: 'divider'
                  }}
                >
                  {column.headerName}
                </TableCell>
              ))}
            </TableRow>
          </TableHead>
          <TableBody>
            {paginatedData.map((row, index) => {
              const isSelected = selectedRows.includes(row.id);
              return (
                <TableRow
                  key={row.id || index}
                  hover
                  selected={isSelected}
                  onClick={(e) => handleRowClick(e, row)}
                  sx={{
                    cursor: 'pointer',
                    '&:hover': {
                      backgroundColor: 'action.hover',
                    },
                    // Remove the background color change on selection
                    // '&.Mui-selected': {
                    //   backgroundColor: 'primary.light',
                    //   '&:hover': {
                    //     backgroundColor: 'primary.main',
                    //   },
                    // },
                  }}
                >
                  {selectable && (
                    <TableCell padding="checkbox">
                      <Checkbox
                        checked={isSelected}
                        onChange={() => handleSelectRow(row.id)}
                      />
                    </TableCell>
                  )}
                  {columns.map((column) => (
                    <TableCell
                      key={column.field}
                      sx={{
                        borderBottom: '1px solid',
                        borderColor: 'divider',
                        py: 1.5
                      }}
                    >
                      {renderCellContent(column, row)}
                    </TableCell>
                  ))}
                </TableRow>
              );
            })}
          </TableBody>
        </Table>
      </TableContainer>

      {/* Pagination */}
      {pagination && (
        <TablePagination
          component="div"
          count={filteredData.length}
          page={page}
          onPageChange={handleChangePage}
          rowsPerPage={rowsPerPage}
          onRowsPerPageChange={handleChangeRowsPerPage}
          rowsPerPageOptions={[5, 10, 25, 50]}
          labelRowsPerPage="Số hàng mỗi trang:"
          labelDisplayedRows={({ from, to, count }) =>
            `${from}-${to} của ${count !== -1 ? count : `hơn ${to}`}`
          }
          sx={{
            borderTop: '1px solid',
            borderColor: 'divider',
            backgroundColor: 'background.paper',
            borderRadius: 2
          }}
        />
      )}

      {/* Action Menu */}
      <Menu
        anchorEl={anchorEl}
        open={Boolean(anchorEl)}
        onClose={handleActionClose}
      >
        <MenuItem onClick={handleActionClose}>
          <ViewIcon fontSize="small" sx={{ mr: 1 }} />
          Xem chi tiết
        </MenuItem>
        <MenuItem onClick={handleActionClose}>
          <EditIcon fontSize="small" sx={{ mr: 1 }} />
          Chỉnh sửa
        </MenuItem>
        <MenuItem onClick={handleActionClose}>
          <ShareIcon fontSize="small" sx={{ mr: 1 }} />
          Chia sẻ
        </MenuItem>
        <MenuItem onClick={handleActionClose} sx={{ color: 'error.main' }}>
          <DeleteIcon fontSize="small" sx={{ mr: 1 }} />
          Xóa
        </MenuItem>
      </Menu>
    </Box>
  );
};

export default DataTable;