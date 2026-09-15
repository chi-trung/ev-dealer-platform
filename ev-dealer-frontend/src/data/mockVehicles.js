/**
 * Mock Vehicle Data — fallback cho vehicleService.js khi API VehicleService
 * (:5068) không reachable (catch-block trong services/vehicleService.js).
 */

export const mockVehicles = [
  {
    id: 1,
    model: "Tesla Model 3",
    type: "sedan",
    price: 45000,
    batteryCapacity: 75,
    range: 350,
    description: "Xe sedan điện cao cấp với tính năng lái xe tự động",
    stockQuantity: 12,
    dealerId: "dealer1",
    dealerName: "Tesla Center HCMC",
    images: [
      "src/assets/img/car1.png",
      "src/assets/img/car2.png",
      "src/assets/img/car3.png"
    ],
    colorVariants: [
      { id: 1, name: "Trắng ngọc trai", hex: "#FFFFFF", stock: 5 },
      { id: 2, name: "Bạc nửa đêm", hex: "#2C2C2C", stock: 4 },
      { id: 3, name: "Xanh dương sâu", hex: "#1E3A8A", stock: 3 }
    ],
    specifications: {
      acceleration: "3.1s 0-60mph",
      topSpeed: "162 mph",
      charging: "250 kW Supercharging",
      warranty: "4 năm/50,000 dặm",
      seats: 5,
      cargo: "15 cu ft"
    },
    createdAt: "2024-01-15T10:30:00Z",
    updatedAt: "2024-01-20T14:45:00Z"
  },
  {
    id: 2,
    model: "Tesla Model Y",
    type: "suv",
    price: 55000,
    batteryCapacity: 75,
    range: 330,
    description: "SUV điện đa năng hoàn hảo cho gia đình",
    stockQuantity: 8,
    dealerId: "dealer1",
    dealerName: "Tesla Center HCMC",
    images: [
      "src/assets/img/car2.png",
      "src/assets/img/car3.png",
      "src/assets/img/car4.png"
    ],
    colorVariants: [
      { id: 4, name: "Trắng ngọc trai", hex: "#FFFFFF", stock: 3 },
      { id: 5, name: "Bạc nửa đêm", hex: "#2C2C2C", stock: 3 },
      { id: 6, name: "Đỏ đa lớp", hex: "#DC2626", stock: 2 }
    ],
    specifications: {
      acceleration: "3.5s 0-60mph",
      topSpeed: "155 mph",
      charging: "250 kW Supercharging",
      warranty: "4 năm/50,000 dặm",
      seats: 7,
      cargo: "76 cu ft"
    },
    createdAt: "2024-01-10T09:15:00Z",
    updatedAt: "2024-01-18T16:20:00Z"
  },
  {
    id: 3,
    model: "BMW i4",
    type: "sedan",
    price: 52000,
    batteryCapacity: 83.9,
    range: 300,
    description: "Xe sedan điện sang trọng với động lực lái BMW đặc trưng",
    stockQuantity: 6,
    dealerId: "dealer2",
    dealerName: "BMW Center District 1",
    images: [
      "src/assets/img/car3.png",
      "src/assets/img/car4.png"
    ],
    colorVariants: [
      { id: 7, name: "Trắng Alpine", hex: "#FFFFFF", stock: 2 },
      { id: 8, name: "Đen Jet", hex: "#000000", stock: 2 },
      { id: 9, name: "Xám khoáng", hex: "#6B7280", stock: 2 }
    ],
    specifications: {
      acceleration: "5.7s 0-60mph",
      topSpeed: "118 mph",
      charging: "200 kW DC Fast Charging",
      warranty: "4 năm/50,000 dặm",
      seats: 5,
      cargo: "17 cu ft"
    },
    createdAt: "2024-01-12T11:45:00Z",
    updatedAt: "2024-01-19T13:30:00Z"
  },
  {
    id: 4,
    model: "Audi e-tron GT",
    type: "sedan",
    price: 105000,
    batteryCapacity: 93.4,
    range: 238,
    description: "Siêu xe điện hiệu suất cao",
    stockQuantity: 3,
    dealerId: "dealer3",
    dealerName: "Audi Center District 2",
    images: [
      "src/assets/img/car4.png",
      "src/assets/img/car1.png"
    ],
    colorVariants: [
      { id: 10, name: "Bạc Florett", hex: "#C0C0C0", stock: 1 },
      { id: 11, name: "Đen Mythos", hex: "#1F2937", stock: 1 },
      { id: 12, name: "Đỏ Tango", hex: "#DC2626", stock: 1 }
    ],
    specifications: {
      acceleration: "3.1s 0-60mph",
      topSpeed: "152 mph",
      charging: "270 kW DC Fast Charging",
      warranty: "4 năm/50,000 dặm",
      seats: 4,
      cargo: "12 cu ft"
    },
    createdAt: "2024-01-08T14:20:00Z",
    updatedAt: "2024-01-17T10:15:00Z"
  },
  {
    id: 5,
    model: "Mercedes EQS",
    type: "sedan",
    price: 120000,
    batteryCapacity: 107.8,
    range: 350,
    description: "Xe sedan điện siêu sang với công nghệ tiên tiến",
    stockQuantity: 2,
    dealerId: "dealer4",
    dealerName: "Mercedes-Benz Center District 3",
    images: [
      "src/assets/img/car1.png",
      "src/assets/img/car2.png"
    ],
    colorVariants: [
      { id: 13, name: "Đen Obsidian", hex: "#000000", stock: 1 },
      { id: 14, name: "Trắng Diamond", hex: "#FFFFFF", stock: 1 }
    ],
    specifications: {
      acceleration: "4.3s 0-60mph",
      topSpeed: "130 mph",
      charging: "200 kW DC Fast Charging",
      warranty: "4 năm/50,000 dặm",
      seats: 5,
      cargo: "22 cu ft"
    },
    createdAt: "2024-01-05T16:30:00Z",
    updatedAt: "2024-01-16T12:45:00Z"
  },
  {
    id: 6,
    model: "Polestar 2",
    type: "sedan",
    price: 49000,
    batteryCapacity: 78,
    range: 270,
    description: "Thiết kế Scandinavian kết hợp hiệu suất điện",
    stockQuantity: 9,
    dealerId: "dealer2",
    dealerName: "BMW Center District 1",
    images: [
      "src/assets/img/car2.png",
      "src/assets/img/car3.png"
    ],
    colorVariants: [
      { id: 15, name: "Trắng Snow", hex: "#FFFFFF", stock: 4 },
      { id: 16, name: "Xám Magnesium", hex: "#8B8B8B", stock: 3 },
      { id: 17, name: "Đen Thunder", hex: "#1F2937", stock: 2 }
    ],
    specifications: {
      acceleration: "4.5s 0-60mph",
      topSpeed: "127 mph",
      charging: "150 kW DC Fast Charging",
      warranty: "4 năm/50,000 dặm",
      seats: 5,
      cargo: "14 cu ft"
    },
    createdAt: "2024-01-14T08:45:00Z",
    updatedAt: "2024-01-21T11:20:00Z"
  },
  {
    id: 7,
    model: "Ford Mustang Mach-E",
    type: "suv",
    price: 43000,
    batteryCapacity: 88,
    range: 320,
    description: "Sức mạnh Mỹ kết hợp đổi mới điện",
    stockQuantity: 11,
    dealerId: "dealer3",
    dealerName: "Audi Center District 2",
    images: [
      "src/assets/img/car4.png",
      "src/assets/img/car1.png",
      "src/assets/img/car2.png"
    ],
    colorVariants: [
      { id: 18, name: "Trắng Star", hex: "#FFFFFF", stock: 5 },
      { id: 19, name: "Xanh Infinite", hex: "#1E40AF", stock: 4 },
      { id: 20, name: "Đen Shadow", hex: "#000000", stock: 2 }
    ],
    specifications: {
      acceleration: "3.5s 0-60mph",
      topSpeed: "140 mph",
      charging: "150 kW DC Fast Charging",
      warranty: "3 năm/36,000 dặm",
      seats: 5,
      cargo: "34 cu ft"
    },
    createdAt: "2024-01-11T13:15:00Z",
    updatedAt: "2024-01-19T09:30:00Z"
  },
  {
    id: 8,
    model: "Hyundai Ioniq 5",
    type: "suv",
    price: 42000,
    batteryCapacity: 77.4,
    range: 303,
    description: "Thiết kế tương lai với nội thất linh hoạt",
    stockQuantity: 7,
    dealerId: "dealer4",
    dealerName: "Mercedes-Benz Center District 3",
    images: [
      "src/assets/img/car3.png",
      "src/assets/img/car4.png"
    ],
    colorVariants: [
      { id: 21, name: "Trắng Atlas", hex: "#FFFFFF", stock: 3 },
      { id: 22, name: "Đen Phantom", hex: "#1F2937", stock: 2 },
      { id: 23, name: "Xanh Lucid", hex: "#3B82F6", stock: 2 }
    ],
    specifications: {
      acceleration: "5.2s 0-60mph",
      topSpeed: "140 mph",
      charging: "350 kW Ultra Fast Charging",
      warranty: "5 năm/60,000 dặm",
      seats: 5,
      cargo: "27 cu ft"
    },
    createdAt: "2024-01-09T10:00:00Z",
    updatedAt: "2024-01-18T14:45:00Z"
  },
  {
    id: 9,
    model: "Kia EV6",
    type: "suv",
    price: 41000,
    batteryCapacity: 77.4,
    range: 310,
    description: "Crossover thể thao với các tính năng cao cấp",
    stockQuantity: 10,
    dealerId: "dealer1",
    dealerName: "Tesla Center HCMC",
    images: [
      "src/assets/img/car1.png",
      "src/assets/img/car2.png",
      "src/assets/img/car3.png",
      "src/assets/img/car4.png"
    ],
    colorVariants: [
      { id: 24, name: "Trắng Snow White Pearl", hex: "#FFFFFF", stock: 4 },
      { id: 25, name: "Xanh Yacht", hex: "#1E40AF", stock: 3 },
      { id: 26, name: "Đen Aurora Black Pearl", hex: "#000000", stock: 3 }
    ],
    specifications: {
      acceleration: "5.1s 0-60mph",
      topSpeed: "140 mph",
      charging: "350 kW Ultra Fast Charging",
      warranty: "5 năm/60,000 dặm",
      seats: 5,
      cargo: "24 cu ft"
    },
    createdAt: "2024-01-13T12:30:00Z",
    updatedAt: "2024-01-20T16:15:00Z"
  },
  {
    id: 10,
    model: "Volkswagen ID.4",
    type: "suv",
    price: 38000,
    batteryCapacity: 82,
    range: 260,
    description: "SUV gia đình thực tế với độ tin cậy Đức",
    stockQuantity: 13,
    dealerId: "dealer2",
    dealerName: "BMW Center District 1",
    images: [
      "src/assets/img/car2.png",
      "src/assets/img/car3.png"
    ],
    colorVariants: [
      { id: 27, name: "Xám Moonstone", hex: "#9CA3AF", stock: 6 },
      { id: 28, name: "Đen Deep Black Pearl", hex: "#000000", stock: 5 },
      { id: 29, name: "Xanh Atlantic", hex: "#1E40AF", stock: 2 }
    ],
    specifications: {
      acceleration: "5.7s 0-60mph",
      topSpeed: "140 mph",
      charging: "125 kW DC Fast Charging",
      warranty: "4 năm/50,000 dặm",
      seats: 5,
      cargo: "30 cu ft"
    },
    createdAt: "2024-01-07T15:20:00Z",
    updatedAt: "2024-01-17T10:45:00Z"
  },
  {
    id: 11,
    model: "Nissan Leaf",
    type: "hatchback",
    price: 28000,
    batteryCapacity: 62,
    range: 226,
    description: "Hatchback điện giá cả phải chăng và đáng tin cậy",
    stockQuantity: 15,
    dealerId: "dealer3",
    dealerName: "Audi Center District 2",
    images: [
      "src/assets/img/car4.png"
    ],
    colorVariants: [
      { id: 30, name: "Trắng Pearl", hex: "#FFFFFF", stock: 7 },
      { id: 31, name: "Xám Gun Metallic", hex: "#6B7280", stock: 5 },
      { id: 32, name: "Bạc Brilliant", hex: "#C0C0C0", stock: 3 }
    ],
    specifications: {
      acceleration: "7.3s 0-60mph",
      topSpeed: "140 mph",
      charging: "100 kW DC Fast Charging",
      warranty: "3 năm/36,000 dặm",
      seats: 5,
      cargo: "23 cu ft"
    },
    createdAt: "2024-01-06T09:10:00Z",
    updatedAt: "2024-01-15T13:25:00Z"
  },
  {
    id: 12,
    model: "Porsche Taycan",
    type: "sedan",
    price: 185000,
    batteryCapacity: 93.4,
    range: 250,
    description: "Hiệu suất xe thể thao điện tối ưu",
    stockQuantity: 1,
    dealerId: "dealer4",
    dealerName: "Mercedes-Benz Center District 3",
    images: [
      "src/assets/img/car1.png",
      "src/assets/img/car2.png",
      "src/assets/img/car3.png"
    ],
    colorVariants: [
      { id: 33, name: "Trắng Carrara", hex: "#FFFFFF", stock: 1 }
    ],
    specifications: {
      acceleration: "2.6s 0-60mph",
      topSpeed: "162 mph",
      charging: "270 kW DC Fast Charging",
      warranty: "4 năm/50,000 dặm",
      seats: 4,
      cargo: "14 cu ft"
    },
    createdAt: "2024-01-04T11:00:00Z",
    updatedAt: "2024-01-14T08:30:00Z"
  }
]

export const mockDealers = [
  {
    id: "dealer1",
    name: "Tesla Center HCMC",
    region: "Ho Chi Minh City",
    contact: "0901234567",
    email: "hcmc@tesla.com",
    address: "123 Nguyen Hue, District 1, HCMC"
  },
  {
    id: "dealer2",
    name: "BMW Center District 1",
    region: "Ho Chi Minh City",
    contact: "0902345678",
    email: "district1@bmw.com",
    address: "456 Le Loi, District 1, HCMC"
  },
  {
    id: "dealer3",
    name: "Audi Center District 2",
    region: "Ho Chi Minh City",
    contact: "0903456789",
    email: "district2@audi.com",
    address: "789 Dong Khoi, District 2, HCMC"
  },
  {
    id: "dealer4",
    name: "Mercedes-Benz Center District 3",
    region: "Ho Chi Minh City",
    contact: "0904567890",
    email: "district3@mercedes.com",
    address: "321 Nguyen Van Cu, District 3, HCMC"
  }
]

export const mockVehicleTypes = [
  { value: "sedan", label: "Sedan" },
  { value: "suv", label: "SUV" },
  { value: "hatchback", label: "Hatchback" },
  { value: "coupe", label: "Coupe" },
  { value: "convertible", label: "Convertible" },
  { value: "truck", label: "Truck" }
]

export const mockVehicleStats = {
  totalVehicles: 31,
  totalValue: 1850000,
  lowStockVehicles: 3,
  topSellingModel: "Tesla Model 3",
  averagePrice: 59677,
  vehiclesByType: {
    sedan: 15,
    suv: 10,
    hatchback: 4,
    coupe: 2
  }
}
