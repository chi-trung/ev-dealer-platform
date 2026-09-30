/**
 * Dropdown/Select Component
 * Supports search, multi-select, and custom options
 */

import { useState, useEffect, useRef } from 'react'

const Dropdown = ({
  options = [],
  value,
  onChange,
  placeholder = 'Select an option',
  searchable = false,
  multiple = false,
  disabled = false,
  error,
  label,
  required = false,
  className = ''
}) => {
  const [isOpen, setIsOpen] = useState(false)
  const [searchTerm, setSearchTerm] = useState('')
  const dropdownRef = useRef(null)

  // Filter options based on search term
  const filteredOptions = searchable 
    ? options.filter(option => 
        option.label.toLowerCase().includes(searchTerm.toLowerCase())
      )
    : options

  // Handle click outside to close dropdown
  useEffect(() => {
    const handleClickOutside = (event) => {
      if (dropdownRef.current && !dropdownRef.current.contains(event.target)) {
        setIsOpen(false)
        setSearchTerm('')
      }
    }

    document.addEventListener('mousedown', handleClickOutside)
    return () => document.removeEventListener('mousedown', handleClickOutside)
  }, [])

  const handleOptionClick = (option) => {
    if (multiple) {
      const currentValues = Array.isArray(value) ? value : []
      const isSelected = currentValues.some(v => v.value === option.value)
      
      if (isSelected) {
        onChange(currentValues.filter(v => v.value !== option.value))
      } else {
        onChange([...currentValues, option])
      }
    } else {
      onChange(option)
      setIsOpen(false)
      setSearchTerm('')
    }
  }

  const handleRemoveTag = (optionToRemove) => {
    if (multiple && Array.isArray(value)) {
      onChange(value.filter(option => option.value !== optionToRemove.value))
    }
  }

  const isSelected = (option) => {
    if (multiple) {
      return Array.isArray(value) && value.some(v => v.value === option.value)
    }
    return value && value.value === option.value
  }

  const getDisplayValue = () => {
    if (multiple) {
      if (!Array.isArray(value) || value.length === 0) {
        return placeholder
      }
      return `${value.length} selected`
    }
    return value ? value.label : placeholder
  }

  const dropdownClasses = [
    'dropdown',
    isOpen ? 'dropdown-open' : '',
    disabled ? 'dropdown-disabled' : '',
    error ? 'dropdown-error' : '',
    className
  ].filter(Boolean).join(' ')

  return (
    <div className="dropdown-group">
      {label && (
        <label className="dropdown-label">
          {label}
          {required && <span className="required">*</span>}
        </label>
      )}
      
      <div className={dropdownClasses} ref={dropdownRef}>
        <div 
          className="dropdown-trigger"
          onClick={() => !disabled && setIsOpen(!isOpen)}
        >
          <div className="dropdown-value">
            {multiple && Array.isArray(value) && value.length > 0 ? (
              <div className="dropdown-tags">
                {value.map((option, index) => (
                  <span key={index} className="dropdown-tag">
                    {option.label}
                    <button 
                      type="button"
                      className="dropdown-tag-remove"
                      onClick={(e) => {
                        e.stopPropagation()
                        handleRemoveTag(option)
                      }}
                    >
                      ×
                    </button>
                  </span>
                ))}
              </div>
            ) : (
              <span className="dropdown-text">{getDisplayValue()}</span>
            )}
          </div>
          <span className="dropdown-arrow">▼</span>
        </div>

        {isOpen && (
          <div className="dropdown-menu">
            {searchable && (
              <div className="dropdown-search">
                <input
                  type="text"
                  placeholder="Search..."
                  value={searchTerm}
                  onChange={(e) => setSearchTerm(e.target.value)}
                  className="dropdown-search-input"
                />
              </div>
            )}
            
            <div className="dropdown-options">
              {filteredOptions.length > 0 ? (
                filteredOptions.map((option, index) => (
                  <div
                    key={index}
                    className={`dropdown-option ${isSelected(option) ? 'selected' : ''}`}
                    onClick={() => handleOptionClick(option)}
                  >
                    <span className="dropdown-option-label">{option.label}</span>
                    {isSelected(option) && (
                      <span className="dropdown-option-check">✓</span>
                    )}
                  </div>
                ))
              ) : (
                <div className="dropdown-no-options">No options found</div>
              )}
            </div>
          </div>
        )}
      </div>
      
      {error && <span className="dropdown-error-message">{error}</span>}
    </div>
  )
}

export default Dropdown
