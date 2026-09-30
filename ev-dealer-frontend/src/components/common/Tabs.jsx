/**
 * Tabs Component
 * Supports different styles, icons, and controlled/uncontrolled modes
 */

import { useState } from 'react'

const Tabs = ({
  tabs = [],
  activeTab,
  onTabChange,
  variant = 'default',
  size = 'medium',
  className = '',
  children
}) => {
  const [internalActiveTab, setInternalActiveTab] = useState(activeTab || tabs[0]?.key)
  
  const currentActiveTab = activeTab !== undefined ? activeTab : internalActiveTab

  const handleTabClick = (tabKey) => {
    if (onTabChange) {
      onTabChange(tabKey)
    } else {
      setInternalActiveTab(tabKey)
    }
  }

  const tabsClasses = [
    'tabs',
    `tabs-${variant}`,
    `tabs-${size}`,
    className
  ].filter(Boolean).join(' ')

  return (
    <div className={tabsClasses}>
      <div className="tabs-header">
        {tabs.map((tab) => (
          <button
            key={tab.key}
            className={`tab-button ${currentActiveTab === tab.key ? 'active' : ''}`}
            onClick={() => handleTabClick(tab.key)}
            disabled={tab.disabled}
          >
            {tab.icon && <span className="tab-icon">{tab.icon}</span>}
            <span className="tab-label">{tab.label}</span>
            {tab.badge && <span className="tab-badge">{tab.badge}</span>}
          </button>
        ))}
      </div>
      
      <div className="tabs-content">
        {children || (
          tabs.find(tab => tab.key === currentActiveTab)?.content
        )}
      </div>
    </div>
  )
}

// Tab Panel Component
export const TabPanel = ({ 
  children, 
  active, 
  className = '' 
}) => {
  if (!active) return null
  
  return (
    <div className={`tab-panel ${className}`}>
      {children}
    </div>
  )
}

// Tab Item Component
export const TabItem = ({
  label,
  icon,
  badge,
  children,
  ...props
}) => {
  return (
    <div className="tab-item" {...props}>
      {icon && <span className="tab-item-icon">{icon}</span>}
      <span className="tab-item-label">{label}</span>
      {badge && <span className="tab-item-badge">{badge}</span>}
      {children}
    </div>
  )
}

// Vertical Tabs Component
export const VerticalTabs = ({
  tabs = [],
  activeTab,
  onTabChange,
  className = '',
  children
}) => {
  const [internalActiveTab, setInternalActiveTab] = useState(activeTab || tabs[0]?.key)
  
  const currentActiveTab = activeTab !== undefined ? activeTab : internalActiveTab

  const handleTabClick = (tabKey) => {
    if (onTabChange) {
      onTabChange(tabKey)
    } else {
      setInternalActiveTab(tabKey)
    }
  }

  return (
    <div className={`vertical-tabs ${className}`}>
      <div className="vertical-tabs-sidebar">
        {tabs.map((tab) => (
          <button
            key={tab.key}
            className={`vertical-tab-button ${currentActiveTab === tab.key ? 'active' : ''}`}
            onClick={() => handleTabClick(tab.key)}
            disabled={tab.disabled}
          >
            {tab.icon && <span className="vertical-tab-icon">{tab.icon}</span>}
            <span className="vertical-tab-label">{tab.label}</span>
            {tab.badge && <span className="vertical-tab-badge">{tab.badge}</span>}
          </button>
        ))}
      </div>
      
      <div className="vertical-tabs-content">
        {children || (
          tabs.find(tab => tab.key === currentActiveTab)?.content
        )}
      </div>
    </div>
  )
}

export default Tabs
